namespace IntentSystem.Cli.Commands;

/// <summary>
/// G335 / G336 / G337 / G338: dispatcher for the <c>guide workflow task</c>
/// subcommand group. Peels the next token (the task name) and
/// delegates to the per-task handler. Tasks today:
/// <list type="bullet">
///   <item><c>init-host</c> (G335) — host/child/review role
///         scaffold plan;</item>
///   <item><c>intent-interview</c> (G336) — product-owner
///         interview / clarification loop guidance;</item>
///   <item><c>packet-draft</c> (G337) — packet files + standalone
///         issue contract;</item>
///   <item><c>issue-publish</c> (G337) — draft / create /
///         publish-flow / automation issue-publish boundary;</item>
///   <item><c>implementation-loop</c> (G338) — paste-ready child
///         implementation-loop prompt from minimal inputs (forwards
///         to <c>guide prompt-matrix --mode child-loop</c>);</item>
///   <item><c>review-next-slice-loop</c> (G338) — paste-ready host
///         review / next-slice-loop prompt from minimal inputs
///         (forwards to <c>guide prompt-matrix --mode host-loop</c>).</item>
/// </list>
/// Future tasks (deploy-host, retire-host, migrate-host) can plug
/// in without touching the router. Pure read-only — never mutates
/// state, never launches an AI provider.
/// </summary>
internal static class GuideWorkflowTaskCommand
{
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

        if (args.Length == 0)
        {
            writer.WriteLine("guide workflow task requires a task name. Supported: init-host, intent-interview, packet-draft, issue-publish, implementation-loop, review-next-slice-loop.");
            WriteHelp(writer);
            return 1;
        }

        var task = args[0];
        return task switch
        {
            "init-host" => GuideWorkflowTaskInitHostCommand.Execute(context, args[1..], writer),
            // G336: intent-interview surfaces the product-owner
            // interview / clarification loop (question structure,
            // commands, stop conditions, durability invariants).
            "intent-interview" => GuideWorkflowTaskIntentInterviewCommand.Execute(context, args[1..], writer),
            // G337: packet-draft explains the four packet files and
            // the standalone issue contract every github-body.md
            // must carry.
            "packet-draft" => GuideWorkflowTaskPacketDraftCommand.Execute(context, args[1..], writer),
            // G337: issue-publish explains the four-stage publish
            // boundary and the intent-target final-boundary rule.
            "issue-publish" => GuideWorkflowTaskIssuePublishCommand.Execute(context, args[1..], writer),
            // G338: implementation-loop generates the paste-ready
            // child implementation-loop prompt from minimal inputs;
            // forwards to `guide prompt-matrix --mode child-loop`.
            "implementation-loop" => GuideWorkflowTaskImplementationLoopCommand.Execute(context, args[1..], writer),
            // G338: review-next-slice-loop generates the paste-ready
            // host review / next-slice-loop prompt from minimal
            // inputs; forwards to `guide prompt-matrix --mode host-loop`.
            "review-next-slice-loop" => GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(context, args[1..], writer),
            _ => UnknownTask(writer, task)
        };
    }

    private static int UnknownTask(TextWriter writer, string task)
    {
        writer.WriteLine($"Unknown 'guide workflow task' name '{task}'. Supported: init-host, intent-interview, packet-draft, issue-publish, implementation-loop, review-next-slice-loop.");
        WriteHelp(writer);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide workflow task");
        writer.WriteLine("Usage: intent-cli guide workflow task <task-name> [task-specific options]");
        writer.WriteLine();
        writer.WriteLine("Tasks:");
        writer.WriteLine("- init-host — explain host/review-runtime/child roles, name where `.intent-cli/` is required/optional/forbidden, emit scaffold plan (AGENTS.md / CLAUDE.md / host-binding.toml). Run with --role <role> for a single role; --force-host to scaffold a host role over an existing child cwd (G335).");
        writer.WriteLine("- intent-interview — product-owner interview / clarification loop guide. Explains the background/question/options/pros-cons/recommendation question structure, distinguishes interview (new concept) from clarification (existing blocker), names the durable artifact paths, lists the canonical commands (G336).");
        writer.WriteLine("- packet-draft — packet directory layout (packet.yaml / implementation.md / review-context.md / github-body.md) + standalone issue contract sections that every github-body.md must satisfy BEFORE `issue publish-flow` (G337).");
        writer.WriteLine("- issue-publish — draft / create / publish-flow / automation issue-publish boundary. Names the four stages, the intent-target FINAL-boundary rule, and the stop conditions that surface missing contract sections before GitHub mutation (G337).");
        writer.WriteLine("- implementation-loop — paste-ready child implementation-loop prompt from minimal inputs (target-repo, agent, frequency, base-branch-policy). Forwards to `guide prompt-matrix --mode child-loop`; carries the current label/claim/complete rules so the operator does not need them from memory (G338).");
        writer.WriteLine("- review-next-slice-loop — paste-ready host review / next-slice-loop prompt from minimal inputs (domain, target-repo, agent, frequency). Forwards to `guide prompt-matrix --mode host-loop`; carries the host-sync preflight gate, automation summary call, and packet/issue lifecycle rules (G338).");
    }
}
