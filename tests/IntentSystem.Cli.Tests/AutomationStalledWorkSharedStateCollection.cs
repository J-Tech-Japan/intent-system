using Xunit;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G532 review repair: <see cref="AutomationStalledWorkCommandTests"/> and
/// <see cref="AutomationHeartbeatCommandTests"/> both mutate the same
/// process-global <c>AutomationStalledWorkCommand.CandidateListerFactory</c>
/// / <c>UtcNowFactory</c> statics (heartbeat wraps
/// <c>AutomationStalledWorkCommand.Analyze</c> directly). Neither class was
/// previously in a shared, non-parallel xUnit collection, so a narrow test
/// filter containing both could race their ctor/Dispose resets — one test's
/// fixed clock or fake lister could be overwritten mid-run by the other
/// class, producing a nondeterministic age/item mismatch. Joining both
/// classes to this collection (see the analogous
/// <see cref="WorkerNextActionSharedStateCollection"/> for the same fix
/// applied to a different static seam) makes xUnit run them sequentially
/// relative to each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AutomationStalledWorkSharedStateCollection
{
    public const string Name = "AutomationStalledWorkSharedState";
}
