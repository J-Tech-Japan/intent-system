namespace IntentSystem.Cli.Commands;

/// <summary>
/// G338: read-only <c>intent-cli guide workflow task review-next-slice-loop</c>.
/// External agents and operators ask for a paste-ready host review /
/// next-slice loop prompt by naming this task — no need to know the
/// underlying <c>guide prompt-matrix --mode host-loop</c> surface.
/// The host loop drives review-runtime workflows for an intent domain,
/// so <c>--domain</c> is the most important input and shows up in the
/// generated automation summary call. The task forwards the operator's
/// minimal inputs to <see cref="GuidePromptMatrixCommand"/>.
/// <para>
/// Pure read-only — emits text only. Never reads parent host queue-state,
/// never calls <c>gh</c>, never mutates state, never launches an AI provider.
/// </para>
/// </summary>
internal static class GuideWorkflowTaskReviewNextSliceLoopCommand
{
    internal const string Mode = "host-loop";

    internal const string UsageLine =
        "Usage: intent-cli guide workflow task review-next-slice-loop [--domain <name>] [--target-repo <owner/repo>] [--agent claude|codex|generic] [--frequency <NNm|NNh>] [--base-branch-policy direct-main|main-ai] [--format markdown|json]";

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

        if (GuideWorkflowTaskLoopForwarder.HasFlag(args, "--mode"))
        {
            writer.WriteLine("--mode is not accepted by `guide workflow task review-next-slice-loop`; the task name pins --mode host-loop.");
            writer.WriteLine(UsageLine);
            return 1;
        }

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
        writer.WriteLine("guide workflow task review-next-slice-loop");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Read-only. Generates the paste-ready host review / next-slice loop prompt from minimal inputs.");
        writer.WriteLine();
        writer.WriteLine("Generated prompt content (host-sync preflight gates, automation summary, packet/issue lifecycle handoff, label transition rules, G314 same-thread scheduler contract) comes from `intent-cli guide prompt-matrix --mode host-loop`. The task here is the discovery entry point — you do not need to remember the prompt-matrix surface.");
        writer.WriteLine();
        writer.WriteLine("Inputs (all optional; placeholders remain when omitted):");
        writer.WriteLine("- --domain <name>                intent domain the host loop owns; appears in `automation summary --domain <d>` and host-side queue lookups.");
        writer.WriteLine("- --target-repo <owner/repo>     concrete repo the host loop reviews; otherwise rendered as <TARGET-REPO>.");
        writer.WriteLine("- --agent claude|codex|generic   chat-first agent driving the loop; controls scheduler phrasing.");
        writer.WriteLine("- --frequency <NNm|NNh>          schedule cadence (e.g. 5m, 20m, 1h); otherwise the prompt asks the operator.");
        writer.WriteLine("- --base-branch-policy direct-main|main-ai  base-branch enforcement; defaults to direct-main.");
        writer.WriteLine("- --format markdown|json         output format; markdown is the default.");
    }
}
