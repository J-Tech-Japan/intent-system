namespace IntentSystem.Cli.Tests;

/// <summary>
/// G464 repair: a non-parallel xUnit collection that serializes every test
/// class which mutates <em>process-global</em> state, guarding two distinct
/// races that surfaced as intermittent CI failures:
///
/// 1. <b>Shared <c>CandidateListerFactory</c> static.</b> Classes that set
///    <see cref="IntentSystem.Cli.Commands.WorkerNextActionCommand.CandidateListerFactory"/>
///    (directly, or indirectly via
///    <see cref="IntentSystem.Cli.Commands.AutomationCheckCommand"/> /
///    <c>automation</c> commands that re-invoke <c>worker next-action</c>).
///    If a parallel class reset that factory between one test's setup and its
///    internal <c>worker next-action</c> call, the internal call fell back to
///    the real GitHub candidate lister, attempted a live <c>gh</c> invocation,
///    and returned exit 1 (observed as
///    <c>AutomationCheck_AndWorkerNextAction_AgreeOnPrCommentFixUnderChildWorkdirContext</c>
///    failing "expected exit code 0, actual 1").
///
/// 2. <b>Process-global current directory.</b> Classes that call
///    <c>Directory.SetCurrentDirectory(workspace.RootPath)</c> and then delete
///    that workspace on dispose. While such a class runs, the process cwd is
///    changed (and soon deleted); any parallel test that reads the cwd
///    (<c>Path.GetFullPath</c> / <c>Directory.GetCurrentDirectory</c>) then
///    threw <c>Interop.Sys.GetCwd</c> (observed as unrelated tests failing with
///    "expected exit code 0, actual 2").
///
/// xUnit parallelizes across test classes by default. <c>DisableParallelization
/// = true</c> makes this collection run with no other collection in parallel,
/// so a cwd mutation or factory reset can never overlap another test —
/// eliminating both races without weakening any assertion. Every class that
/// touches either piece of global state must join this collection.
/// </summary>
[CollectionDefinition("WorkerNextActionSharedState", DisableParallelization = true)]
public sealed class WorkerNextActionSharedStateCollection
{
}
