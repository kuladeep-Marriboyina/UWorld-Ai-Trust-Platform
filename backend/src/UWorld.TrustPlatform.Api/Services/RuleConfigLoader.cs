using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;

namespace UWorld.TrustPlatform.Api.Services;

/// <summary>Reads RULE_DEFINITIONS into the in-memory RuleConfigStore. Called at startup and after
/// any portal change (toggle / activate a new rule) so the engine picks it up with no redeploy.</summary>
public sealed class RuleConfigLoader
{
    private readonly IRuleRepository _repo;
    private readonly RuleConfigStore _store;

    public RuleConfigLoader(IRuleRepository repo, RuleConfigStore store)
    {
        _repo = repo;
        _store = store;
    }

    public async Task ReloadAsync(CancellationToken ct = default)
        => _store.Reload(await _repo.GetConfigsAsync(ct));
}
