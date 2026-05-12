namespace IntentSystem.Cli.Commands;

/// <summary>
/// G338: read-only <c>intent-cli guide workflow task implementation-loop</c>.
/// External agents that have never seen the project can ask for a
/// paste-ready child implementation loop prompt by naming this task —
/// no need to know the underlying <c>guide prompt-matrix --mode child-loop</c>
/// surface. The task accepts the minimal operator-facing inputs
/// (target repo, agent, frequency, base-branch policy) and forwards to
/// <see cref="GuidePromptMatrixCommand"/>. Domain is accepted as an
/// optional hint for placeholders inside the generated prompt; the
/// child loop itself does not require host-side domain metadata.
/// <para>
/// Pure read-only — emits text only. Never reads parent host queue-state,
/// never calls <c>gh</c>, never mutates state, never launches an AI provider.
/// </para>
/// </summary>
internal static class GuideWorkflowTaskImplementationLoopCommand
{
    internal const string Mode = "child-loop";

    internal const string UsageLine =
        "Usage: intent-cli guide workflow task implementation-loop [--target-repo <owner/repo>] [--agent claude|codex|generic] [--frequency <NNm|NNh>] [--base-branch-policy direct-main|main-ai] [--domain <name>] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        // Reject --mode because the task name pins it to child-loop.
        if (GuideWorkflowTaskLoopForwarder.HasFlag(args, "--mode"))
        {
            writer.WriteLine("--mode is not accepted by `guide workflow task implementation-loop`; the task name pins --mode child-loop.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        // Pre-validate flag shape so unknown-flag errors surface the
        // wrapper's usage line, not the underlying prompt-matrix one.
        var unknownFlag = GuideWorkflowTaskLoopForwarder.FindFirstUnknownFlag(args);
        if (unknownFlag is not null)
        {
            writer.WriteLine($"Unknown argument '{unknownFlag}'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var forwarded = GuideWorkflowTaskLoopForwarder.PrependMode(args, Mode);
        return GuidePromptMatrixCommand.Execute(context, forwarded, writer);
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide workflow task implementation-loop");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Read-only. Generates the paste-ready child implementation-loop prompt from minimal inputs.");
        writer.WriteLine();
        writer.WriteLine("Generated prompt content (label transitions, claim/complete contract, G300/G330/G333 child-cwd rules, G314 same-thread scheduler contract, G311 closing reference, base-branch policy enforcement) comes from `intent-cli guide prompt-matrix --mode child-loop`. The task here is the discovery entry point — you do not need to remember the prompt-matrix surface.");
        writer.WriteLine();
        writer.WriteLine("Inputs (all optional; placeholders remain when omitted):");
        writer.WriteLine("- --target-repo <owner/repo>     concrete repo the child loop drives; otherwise rendered as <TARGET-REPO>.");
        writer.WriteLine("- --agent claude|codex|generic   chat-first agent driving the loop; controls scheduler phrasing.");
        writer.WriteLine("- --frequency <NNm|NNh>          schedule cadence (e.g. 5m, 20m, 1h); otherwise the prompt asks the operator.");
        writer.WriteLine("- --base-branch-policy direct-main|main-ai  base-branch enforcement; defaults to direct-main.");
        writer.WriteLine("- --domain <name>                hint for prompt placeholders; child loop does not require host-side domain metadata.");
        writer.WriteLine("- --format markdown|json         output format; markdown is the default.");
    }
}
