using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public interface IDevRequestLog
{
    Task AddAsync(DevRequestEntry e, CancellationToken ct = default);
    Task<IReadOnlyList<DevRequestEntry>> ListAsync(int take, string? status, string? search, CancellationToken ct = default);
    Task<DevRequestEntry?> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<DevRequestEntry>> GetForCaseAsync(int caseId, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class DevRequestLogRepository : IDevRequestLog
{
    private const string Columns =
        @"Id, RequestId, Method, Path, QueryString, RequestHeaders, RequestBody, StatusCode, ResponseHeaders, ResponseBody,
          DurationMs, Role, Controller, Action, RoutePattern, ExceptionType, ExceptionMessage, StackTrace, OccurredUtc";

    private readonly ISqlConnectionFactory _factory;

    public DevRequestLogRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(DevRequestEntry e, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            @"INSERT dbo.DevRequestLog (RequestId, Method, Path, QueryString, RequestHeaders, RequestBody, StatusCode, ResponseHeaders, ResponseBody,
                                        DurationMs, Role, Controller, Action, RoutePattern, ExceptionType, ExceptionMessage, StackTrace, OccurredUtc)
              VALUES (@RequestId, @Method, @Path, @QueryString, @RequestHeaders, @RequestBody, @StatusCode, @ResponseHeaders, @ResponseBody,
                      @DurationMs, @Role, @Controller, @Action, @RoutePattern, @ExceptionType, @ExceptionMessage, @StackTrace, @OccurredUtc)", cn);
        cmd.Parameters.AddWithValue("@RequestId", e.RequestId);
        cmd.Parameters.AddWithValue("@Method", e.Method);
        cmd.Parameters.AddWithValue("@Path", e.Path);
        cmd.Parameters.AddWithValue("@QueryString", (object?)e.QueryString ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RequestHeaders", (object?)e.RequestHeaders ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RequestBody", (object?)e.RequestBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StatusCode", e.StatusCode);
        cmd.Parameters.AddWithValue("@ResponseHeaders", (object?)e.ResponseHeaders ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ResponseBody", (object?)e.ResponseBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DurationMs", e.DurationMs);
        cmd.Parameters.AddWithValue("@Role", (object?)e.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Controller", (object?)e.Controller ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Action", (object?)e.Action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RoutePattern", (object?)e.RoutePattern ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExceptionType", (object?)e.ExceptionType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExceptionMessage", (object?)e.ExceptionMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StackTrace", (object?)e.StackTrace ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OccurredUtc", e.OccurredUtc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DevRequestEntry>> ListAsync(int take, string? status, string? search, CancellationToken ct = default)
    {
        // status: null/"all", "errors" (>=400), "4xx", "5xx", "2xx"
        var sql = $@"SELECT TOP (@Take) {Columns} FROM dbo.DevRequestLog
                     WHERE (@Status IS NULL
                            OR (@Status = 'errors' AND StatusCode >= 400)
                            OR (@Status = '2xx' AND StatusCode BETWEEN 200 AND 299)
                            OR (@Status = '4xx' AND StatusCode BETWEEN 400 AND 499)
                            OR (@Status = '5xx' AND StatusCode >= 500))
                       AND (@Search IS NULL OR Path LIKE '%' + @Search + '%' OR RequestId = @Search)
                     ORDER BY Id DESC";
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Take", take);
        cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(status) || status == "all" ? DBNull.Value : status);
        cmd.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());

        var rows = new List<DevRequestEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(Map(r));
        return rows;
    }

    public async Task<DevRequestEntry?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand($"SELECT {Columns} FROM dbo.DevRequestLog WHERE Id = @Id", cn);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
    }

    /// <summary>Captured HTTP calls whose RequestId produced this case (joined via the ledger's CaseRaised rows).</summary>
    public async Task<IReadOnlyList<DevRequestEntry>> GetForCaseAsync(int caseId, CancellationToken ct = default)
    {
        var sql = $@"SELECT TOP 5 {Columns} FROM dbo.DevRequestLog
                     WHERE RequestId IN (SELECT RequestId FROM dbo.RequestLedger WHERE CaseId = @CaseId)
                     ORDER BY CASE WHEN Method <> 'GET' THEN 0 ELSE 1 END, Id DESC";
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@CaseId", caseId);
        var rows = new List<DevRequestEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(Map(r));
        return rows;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("TRUNCATE TABLE dbo.DevRequestLog", cn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static DevRequestEntry Map(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0), RequestId = r.GetString(1), Method = r.GetString(2), Path = r.GetString(3),
        QueryString = Str(r, 4), RequestHeaders = Str(r, 5), RequestBody = Str(r, 6), StatusCode = r.GetInt32(7),
        ResponseHeaders = Str(r, 8), ResponseBody = Str(r, 9), DurationMs = r.GetInt32(10), Role = Str(r, 11),
        Controller = Str(r, 12), Action = Str(r, 13), RoutePattern = Str(r, 14), ExceptionType = Str(r, 15),
        ExceptionMessage = Str(r, 16), StackTrace = Str(r, 17), OccurredUtc = r.GetDateTime(18)
    };

    private static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
