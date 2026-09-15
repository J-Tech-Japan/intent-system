namespace IntentSystem.Cli.Tests;

/// <summary>
/// G834: <c>AutomationPrTransitionCommand.MutatorFactory</c> is assigned by the
/// original pr-transition tests and by the cross-runtime review gate tests, so
/// both classes share one xUnit collection (G580 static-seam serialization).
/// </summary>
[CollectionDefinition(Name)]
public sealed class AutomationPrTransitionSharedStateCollection
{
    public const string Name = "AutomationPrTransitionSharedState";
}
