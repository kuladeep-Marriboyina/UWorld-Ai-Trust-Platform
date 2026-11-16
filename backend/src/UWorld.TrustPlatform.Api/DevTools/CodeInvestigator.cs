using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.DevTools;

public sealed record CodeSnippet(string File, int StartLine, int? HighlightLine, IReadOnlyList<string> Lines);
public sealed record CodeStep(string Layer, string Symbol, CodeSnippet Snippet);
public sealed record DevFinding(string Severity, string Title, string Detail, CodeSnippet? Snippet);

public sealed record Investigation(
    long RequestLogId,
    string Verdict,              // "bug" | "client-error" | "ok"
    string Summary,
    string? LikelyCause,
    string? SuggestedFix,
    CodeSnippet? Location,
    IReadOnlyList<CodeStep> CodePath,
    IReadOnlyList<DevFinding> Findings,
    int FilesScanned);

/// <summary>
/// Walks the real source code behind one captured request: the controller action that handled it,
/// then the services and repositories it calls. For a failed request it pins the failing line from
/// the stack trace and explains the likely cause; for any request it flags risky patterns on that
/// path. Deterministic and local — an LLM can later be added behind the same result shape.
/// </summary>
public sealed partial class CodeInvestigator
{
    private const int MaxSteps = 8;
    private readonly string _sourceRoot;
    private readonly string _repoRoot;
    private readonly EndpointDataSource _endpoints;

    public CodeInvestigator(IWebHostEnvironment env, IConfiguration config, EndpointDataSource endpoints)
    {
        // ContentRoot is backend/src/UWorld.TrustPlatform.Api → the source root is its parent.
        _sourceRoot = config["DevTools:SourceRoot"] ?? Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));
        _repoRoot = Path.GetFullPath(Path.Combine(_sourceRoot, "..", ".."));
        _endpoints = endpoints;
    }

    public Investigation Investigate(DevRequestEntry req)
    {
        var index = SourceIndex.Build(_sourceRoot);
        var path = new List<CodeStep>();
        var findings = new List<DevFinding>();

        // 1) Code path: controller action → the services/repositories it calls (two levels down).
        string? controllerFile = null;
        MethodBlock? actionBlock = null;
        if (req.Controller is not null && req.Action is not null)
        {
            var controllerName = req.Controller.Split('.').Last();
            if (index.TypeFiles.TryGetValue(controllerName, out controllerFile))
            {
                actionBlock = FindMethod(index.Lines(controllerFile), req.Action, includeAttributes: true);
                if (actionBlock is not null)
                {
                    path.Add(new CodeStep("Controller", $"{controllerName}.{req.Action}", Snip(controllerFile, actionBlock, null)));
                    FollowCalls(index, controllerFile, actionBlock, path, depth: 1);
                }
            }
        }

        // 2) Exception: pin the first frame that is in our own code.
        CodeSnippet? location = null;
        string? cause = null, fix = null;
        if (req.StackTrace is not null)
        {
            foreach (Match m in FrameRegex().Matches(req.StackTrace))
            {
                var file = m.Groups["file"].Value.Trim();
                var line = int.Parse(m.Groups["line"].Value);
                if (!File.Exists(file) || !Path.GetFullPath(file).StartsWith(_sourceRoot, StringComparison.OrdinalIgnoreCase)) continue;
                location = Window(file, line, 7);
                break;
            }
            (cause, fix) = ExplainException(req, location);
            findings.Add(new DevFinding("error", $"{ShortType(req.ExceptionType)} thrown",
                req.ExceptionMessage ?? "(no message)", location));
        }

        // 3) Non-exception failures: explain from the status code and the code that produced it.
        var verdict = "ok";
        if (req.StatusCode >= 500) verdict = "bug";
        else if (req.StatusCode >= 400) verdict = "client-error";

        if (req.StackTrace is null && req.StatusCode >= 400)
            (cause, fix, location) = ExplainStatus(req, index, controllerFile, actionBlock);

        if (req.DurationMs >= 1000)
            findings.Add(new DevFinding("warning", $"Slow request: {req.DurationMs:N0} ms",
                "Anything over a second is worth a look. Check the queries on the code path below for missing indexes or row-by-row loops.", null));

        // 4) Risky patterns on the code path.
        foreach (var step in path)
            findings.AddRange(ScanForSmells(step));

        var summary = verdict switch
        {
            "bug" => $"{req.Method} {req.Path} failed with {req.StatusCode}" + (location is null ? "." : $" at {location.File}:{location.HighlightLine}.") + (cause is null ? " The server hit an error it did not handle." : ""),
            "client-error" => $"{req.Method} {req.Path} was rejected with {req.StatusCode}. The server code worked as written; the request needs to change (or the rule it hit does)." ,
            _ => $"{req.Method} {req.Path} succeeded ({req.StatusCode}) in {req.DurationMs:N0} ms. No error in this request; the code path and any risky patterns on it are listed below."
        };

        return new Investigation(req.Id, verdict, summary, cause, fix, location, path,
            findings.GroupBy(f => (f.Title, f.Snippet?.File, f.Snippet?.HighlightLine)).Select(g => g.First()).ToList(),
            index.FileCount);
    }

    // ------------------------------------------------------------------ code path

    private void FollowCalls(SourceIndex index, string file, MethodBlock block, List<CodeStep> path, int depth)
    {
        if (depth > 2 || path.Count >= MaxSteps) return;
        var lines = index.Lines(file);
        var fileText = string.Join('\n', lines);
        var body = string.Join('\n', lines.Skip(block.Start).Take(block.End - block.Start + 1));

        foreach (var call in CallRegex().Matches(body).Select(m => (Field: m.Groups[1].Value, Method: m.Groups[2].Value)).Distinct())
        {
            if (path.Count >= MaxSteps) return;
            var fieldType = Regex.Match(fileText, $@"(\w+)(?:<[^>]*>)?\??\s+{Regex.Escape(call.Field)}\s*[;=]");
            if (!fieldType.Success) continue;
            var type = fieldType.Groups[1].Value;
            var implFile = index.ImplementationOf(type);
            if (implFile is null) continue;
            var target = FindMethod(index.Lines(implFile), call.Method, includeAttributes: false);
            if (target is null) continue;
            var implName = index.TypeNameIn(implFile, type) ?? type;
            if (path.Any(p => p.Symbol == $"{implName}.{call.Method}")) continue;
            path.Add(new CodeStep(LayerOf(implFile), $"{implName}.{call.Method}", Snip(implFile, target, null)));
            FollowCalls(index, implFile, target, path, depth + 1);
        }
    }

    private static string LayerOf(string file) =>
        file.Contains(".Data", StringComparison.OrdinalIgnoreCase) ? "Repository"
        : file.Contains(".Detection", StringComparison.OrdinalIgnoreCase) ? "Detection"
        : file.Contains(".Ai", StringComparison.OrdinalIgnoreCase) ? "AI"
        : file.Contains(".Ingestion", StringComparison.OrdinalIgnoreCase) ? "Ingestion"
        : "Service";

    private sealed record MethodBlock(int Start, int Decl, int End);

    /// <summary>Finds a method declaration and its body (brace-matched, or an expression body up to ';').</summary>
    private static MethodBlock? FindMethod(string[] lines, string name, bool includeAttributes)
    {
        var decl = new Regex($@"^\s*(public|private|protected|internal)[^=;(]*\b{Regex.Escape(name)}\s*(<[^>]*>)?\s*\(");
        for (var i = 0; i < lines.Length; i++)
        {
            if (!decl.IsMatch(lines[i])) continue;
            var start = i;
            if (includeAttributes)
                while (start > 0 && lines[start - 1].TrimStart().StartsWith("[")) start--;

            int depth = 0, end = i;
            var opened = false;
            for (var j = i; j < lines.Length && j < i + 80; j++)
            {
                foreach (var ch in lines[j])
                {
                    if (ch == '{') { depth++; opened = true; }
                    else if (ch == '}') depth--;
                }
                end = j;
                if (opened && depth <= 0) break;
                if (!opened && lines[j].TrimEnd().EndsWith(";") && (lines[j].Contains("=>") || j > i)) break;
            }
            return new MethodBlock(start, i, end);
        }
        return null;
    }

    private CodeSnippet Snip(string file, MethodBlock b, int? highlight) =>
        new(Rel(file), b.Start + 1, highlight, File.ReadAllLines(file).Skip(b.Start).Take(b.End - b.Start + 1).ToList());

    private CodeSnippet Window(string file, int line, int radius)
    {
        var all = File.ReadAllLines(file);
        var from = Math.Max(1, line - radius);
        var to = Math.Min(all.Length, line + radius);
        return new CodeSnippet(Rel(file), from, line, all.Skip(from - 1).Take(to - from + 1).ToList());
    }

    private string Rel(string file) => Path.GetRelativePath(_repoRoot, file).Replace('\\', '/');

    // ------------------------------------------------------------------ explanations

    private static (string? cause, string? fix) ExplainException(DevRequestEntry req, CodeSnippet? at)
    {
        var type = ShortType(req.ExceptionType);
        var msg = req.ExceptionMessage ?? "";
        var where = at is null ? "" : $" at {at.File}:{at.HighlightLine}";

        if (type == "SqlException")
        {
            var col = Regex.Match(msg, @"Invalid column name '([^']+)'");
            if (col.Success) return ($"The SQL{where} names a column '{col.Groups[1].Value}' that does not exist — the query and the database schema are out of step.",
                                     $"Re-run the database scripts, or fix the column name in the query to match the table.");
            var obj = Regex.Match(msg, @"Invalid object name '([^']+)'");
            if (obj.Success) return ($"The table or procedure '{obj.Groups[1].Value}' does not exist in the database the API is connected to.",
                                     "Run the missing database script (see the database folder) or check the connection string.");
            if (msg.Contains("TOP N", StringComparison.OrdinalIgnoreCase) || msg.Contains("FETCH rowcount", StringComparison.OrdinalIgnoreCase))
                return ($"A row-count value from the request ({InputsFrom(req)}) reached the SQL TOP/FETCH clause without validation, and SQL Server rejects negative values{where}.",
                        $"Validate the input in {req.Controller?.Split('.').Last()}.{req.Action} (e.g. Math.Clamp(top, 1, 500)) and return 400 for out-of-range values.");
            if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                return ($"The database query{where} timed out.", "Check the query plan and indexes; add a WHERE clause or paging.");
            if (msg.Contains("conversion failed", StringComparison.OrdinalIgnoreCase) || msg.Contains("converting", StringComparison.OrdinalIgnoreCase))
                return ($"SQL Server could not convert a value{where}: a parameter or column has the wrong type.", "Match the parameter's .NET type to the column type.");
            return ($"SQL Server rejected the command{where}: {msg}", "Run the statement directly in SQL Server to see the exact failure.");
        }

        return type switch
        {
            "NullReferenceException" => ($"Something was null when the code{where} used it.",
                "Check which value on the highlighted line can be null (a lookup that found nothing, an optional body field) and guard it or return 404/400."),
            "InvalidCastException" => ($"A value was read or cast as the wrong type{where} — often a SQL column read with GetInt32 when it is BIGINT/DECIMAL, or (int) on ExecuteScalar.",
                "Use Convert.ToInt32(...) or the reader method matching the column's SQL type."),
            "InvalidOperationException" when msg.Contains("Sequence contains", StringComparison.OrdinalIgnoreCase) =>
                ($"First()/Single() ran on an empty or duplicate sequence{where}.", "Use FirstOrDefault() and handle the empty case."),
            "InvalidOperationException" => ($"An operation was called in an invalid state{where}: {msg}", "Read the message and the highlighted line; check object lifetime and ordering."),
            "ArgumentException" when msg.Contains("same key", StringComparison.OrdinalIgnoreCase) =>
                ($"ToDictionary/Add hit a duplicate key{where}.", "Group first (GroupBy → First) or use a lookup that tolerates duplicates."),
            "KeyNotFoundException" => ($"A dictionary lookup{where} used a key that is not there.", "Use TryGetValue and handle the missing key."),
            "FormatException" => ($"Text could not be parsed into a number or date{where}.", "Use TryParse and return 400 for bad input."),
            "JsonException" => ($"The JSON body or a stored JSON value could not be read{where}.", "Compare the request payload with the expected model; validate before deserialising."),
            "ArgumentNullException" => ($"A required argument was null{where}.", "Validate inputs at the controller and return 400."),
            "DivideByZeroException" => ($"A division by zero happened{where}.", "Guard the denominator."),
            "OperationCanceledException" or "TaskCanceledException" => ("The request was cancelled (client disconnected or a timeout fired).", "Usually not a bug; check timeouts if it happens often."),
            _ => ($"{type}{where}: {msg}", "Start from the highlighted line and the stack trace below.")
        };
    }

    private (string? cause, string? fix, CodeSnippet? at) ExplainStatus(DevRequestEntry req, SourceIndex index, string? controllerFile, MethodBlock? action)
    {
        var bodyError = ErrorFromBody(req.ResponseBody);

        if (req.Controller is null)
        {
            var near = NearestRoutes(req.Path);
            return ($"No endpoint matches {req.Method} {req.Path}.",
                    near.Count > 0 ? "Closest routes the API does serve: " + string.Join(" · ", near) : "Check the URL and HTTP method.", null);
        }

        CodeSnippet? Highlight(params string[] needles)
        {
            if (controllerFile is null || action is null) return null;
            var lines = index.Lines(controllerFile);
            for (var i = action.Start; i <= action.End; i++)
                if (needles.Any(n => lines[i].Contains(n))) return Window(controllerFile, i + 1, 5);
            return Snip(controllerFile, action, null);
        }

        return req.StatusCode switch
        {
            403 => ($"Role '{req.Role ?? "none"}' is not allowed to call {req.Action}. {bodyError}".Trim(),
                    "Send an allowed role in the X-Role header, or change the [RequireRole] list if this role should be allowed.",
                    Highlight("[RequireRole")),
            404 => ($"The route matched {req.Action}, but the record was not found (the action returned NotFound).",
                    "Check the id in the URL exists; if it should, check the repository query shown in the code path.",
                    Highlight("NotFound(")),
            400 => ($"The request was rejected as invalid. {bodyError ?? ValidationFromBody(req.ResponseBody)}".Trim(),
                    "Compare the request payload with the model the action expects (shown in the code path).",
                    Highlight("BadRequest(", "[FromBody]")),
            405 => ("The URL exists but not for this HTTP method.", "Use the method shown on the action's [Http…] attribute.", Highlight("[Http")),
            415 => ("The body's Content-Type is not accepted.", "Send Content-Type: application/json.", Highlight("[FromBody]")),
            _ => ($"The action returned {req.StatusCode}. {bodyError}".Trim(), null, Highlight("return"))
        };
    }

    private List<string> NearestRoutes(string path)
    {
        var segs = path.Trim('/').Split('/');
        return _endpoints.Endpoints.OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? "")
            .Where(r => r.StartsWith("api", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Select(r => (Route: "/" + r, Score: r.Split('/').Zip(segs, SegmentScore).Sum() - Math.Abs(r.Split('/').Length - segs.Length) * 0.5))
            .Where(x => x.Score >= 2)
            .OrderByDescending(x => x.Score).Take(5).Select(x => x.Route).ToList();
    }

    /// <summary>1 for an exact or near segment match (typo, plural, route parameter), else 0.</summary>
    private static double SegmentScore(string route, string actual)
    {
        if (string.Equals(route, actual, StringComparison.OrdinalIgnoreCase)) return 1;
        if (route.StartsWith('{')) return 0.8;
        if (route.StartsWith(actual, StringComparison.OrdinalIgnoreCase) || actual.StartsWith(route, StringComparison.OrdinalIgnoreCase)) return 0.9;
        return 0;
    }

    private static string? ErrorFromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e)) return e.ToString();
        }
        catch (JsonException) { }
        return null;
    }

    private static string? ValidationFromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("errors", out var errs))
                return string.Join(" ", errs.EnumerateObject().Select(p => $"{p.Name}: {string.Join(", ", p.Value.EnumerateArray().Select(v => v.GetString()))}"));
        }
        catch (JsonException) { }
        return null;
    }

    private static string InputsFrom(DevRequestEntry req) =>
        string.IsNullOrEmpty(req.QueryString) ? "a request value" : req.QueryString.TrimStart('?').Replace("&", ", ");

    private static string ShortType(string? t) => t?.Split('.').Last() ?? "Exception";

    // ------------------------------------------------------------------ pattern scan

    private static IEnumerable<DevFinding> ScanForSmells(CodeStep step)
    {
        var s = step.Snippet;
        for (var i = 0; i < s.Lines.Count; i++)
        {
            var line = s.Lines[i];
            var at = s.StartLine + i;
            CodeSnippet Here() => new(s.File, Math.Max(s.StartLine, at - 2), at, s.Lines.Skip(Math.Max(0, i - 2)).Take(5).ToList());

            if (SqlInterpolationRegex().IsMatch(line) && !line.Contains("{Columns}") && !line.Contains("{column}") && !line.Contains("{table}"))
                yield return new DevFinding("warning", "SQL built with string interpolation",
                    $"{step.Symbol}: values pasted into SQL text can allow SQL injection. Use @parameters instead.", Here());
            if (Regex.IsMatch(line, @"\.Result\b|\.Wait\(\)|\.GetAwaiter\(\)\.GetResult\(\)"))
                yield return new DevFinding("warning", "Blocking on async code",
                    $"{step.Symbol}: .Result / .Wait() can deadlock or starve threads. Use await.", Here());
            if (Regex.IsMatch(line, @"catch(\s*\([^)]*\))?\s*\{\s*(/\*.*\*/|//.*)?\s*\}"))
                yield return new DevFinding("info", "Exception swallowed",
                    $"{step.Symbol}: an empty catch hides failures. Log it or let it surface.", Here());
            if (Regex.IsMatch(line, @"\.(First|Single)\(") && !line.Contains("OrDefault"))
                yield return new DevFinding("info", "First()/Single() can throw",
                    $"{step.Symbol}: throws when nothing (or more than one item) matches. Use FirstOrDefault() if empty is possible.", Here());
            if (line.Contains(".ToDictionary("))
                yield return new DevFinding("info", "ToDictionary can throw on duplicate keys",
                    $"{step.Symbol}: if the key repeats in the data this throws ArgumentException.", Here());
            if (Regex.IsMatch(line, @"\(int\)\s*\(?\s*await\s+\w+\.ExecuteScalarAsync"))
                yield return new DevFinding("info", "Hard cast on ExecuteScalar",
                    $"{step.Symbol}: (int) fails if SQL returns BIGINT, DECIMAL or NULL. Use Convert.ToInt32.", Here());
            if (Regex.IsMatch(line, @"new\s+SqlConnection\(") && !line.Contains("using") && !step.Symbol.Contains("ConnectionFactory"))
                yield return new DevFinding("info", "Connection without using",
                    $"{step.Symbol}: make sure this connection is disposed, or it leaks from the pool.", Here());
        }
    }

    [GeneratedRegex(@" in (?<file>[A-Za-z]:[^:\r\n]+\.cs|/[^:\r\n]+\.cs):line (?<line>\d+)")]
    private static partial Regex FrameRegex();

    [GeneratedRegex(@"\b(_\w+)\.(\w+)\(")]
    private static partial Regex CallRegex();

    [GeneratedRegex(@"\$@?""[^""]*\b(SELECT|INSERT|UPDATE|DELETE|WHERE)\b[^""]*\{[^}]+\}", RegexOptions.IgnoreCase)]
    private static partial Regex SqlInterpolationRegex();

    // ------------------------------------------------------------------ source index

    private sealed class SourceIndex
    {
        private readonly Dictionary<string, string[]> _lines = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> TypeFiles { get; } = new(StringComparer.Ordinal);
        private readonly List<(string File, string Type, string Bases)> _types = new();
        public int FileCount => _lines.Count;

        public static SourceIndex Build(string root)
        {
            var idx = new SourceIndex();
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                var lines = File.ReadAllLines(file);
                idx._lines[file] = lines;
                foreach (Match m in Regex.Matches(string.Join('\n', lines), @"\b(?:class|record|interface|struct)\s+(\w+)(?:<[^>]*>)?(?:\([^)]*\))?\s*(?::\s*([^{\n]+))?"))
                {
                    var name = m.Groups[1].Value;
                    idx.TypeFiles.TryAdd(name, file);
                    idx._types.Add((file, name, m.Groups[2].Value));
                }
            }
            return idx;
        }

        public string[] Lines(string file) => _lines.TryGetValue(file, out var l) ? l : File.ReadAllLines(file);

        /// <summary>The file of a concrete type implementing the interface (or the type itself).</summary>
        public string? ImplementationOf(string type)
        {
            var impl = _types.FirstOrDefault(t => Regex.IsMatch(t.Bases, $@"\b{Regex.Escape(type)}\b") && !t.Type.StartsWith("I", StringComparison.Ordinal));
            if (impl.File is not null) return impl.File;
            return TypeFiles.TryGetValue(type, out var f) ? f : null;
        }

        public string? TypeNameIn(string file, string iface) =>
            _types.FirstOrDefault(t => t.File == file && Regex.IsMatch(t.Bases, $@"\b{Regex.Escape(iface)}\b")).Type
            ?? _types.FirstOrDefault(t => t.File == file).Type;
    }
}
