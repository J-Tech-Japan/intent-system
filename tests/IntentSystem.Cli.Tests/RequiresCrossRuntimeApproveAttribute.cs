namespace IntentSystem.Cli.Tests;

/// <summary>
/// G835: marks a gate test that documents the shared cross-runtime approve
/// requirement. A build-guarded mutation that removes the cross-runtime check
/// from <c>CrossRuntimeReviewGate.BuildDecision</c> must fail every test
/// carrying this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequiresCrossRuntimeApproveAttribute : Attribute;
