using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Data;

namespace UWorld.TrustPlatform.Api.Services;

/// <summary>
/// Puts the demo database back to its seeded state by re-running the seed scripts (03, 05, 07), so every
/// rehearsal and every live demo starts from the same baseline: realistic history, no leftover simulations.
/// </summary>
public sealed partial class DemoResetService
{
    private static readonly string[] Scripts = { "03_seed.sql", "05_fraud_seed.sql", "07_trust_v2.sql" };
    private readonly ISqlConnectionFactory _factory;
    private readonly IWebHostEnvironment _env;

    public DemoResetService(ISqlConnectionFactory factory, IWebHostEnvironment env)
    {
        _factory = factory;
        _env = env;
    }

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)] private static partial Regex GoSeparator();

    public string? DatabaseFolder()
    {
        var dir = new DirectoryInfo(_env.ContentRootPath);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "database");
            if (File.Exists(Path.Combine(candidate, "07_trust_v2.sql"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public async Task<IReadOnlyList<string>> ResetAsync(CancellationToken ct)
    {
        var folder = DatabaseFolder() ?? throw new InvalidOperationException("Couldn't find the database/ folder with the seed scripts.");
        var ran = new List<string>();
        await using var cn = await _factory.OpenAsync(ct);
        foreach (var script in Scripts)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(folder, script), ct);
            foreach (var batch in GoSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var cmd = new SqlCommand(batch, cn) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct);
            }
            ran.Add(script);
        }
        return ran;
    }
}
