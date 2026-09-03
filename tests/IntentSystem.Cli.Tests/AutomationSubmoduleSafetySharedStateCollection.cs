namespace IntentSystem.Cli.Tests;

/// <summary>
/// Serializes the command tests that replace the process-global Git seams used
/// by the G306/G791 host safety commands. The G791 production topology tests
/// intentionally exercise the real shell runners, so they must not overlap a
/// synthetic test that has installed a fake runner or probe.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AutomationSubmoduleSafetySharedStateCollection
{
    public const string Name = "AutomationSubmoduleSafetySharedState";
}
