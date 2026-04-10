namespace IntentSystem.Cli.Commands;

internal static class CommandRouter
{
    private delegate int CommandHandler(CliContext context, string[] args, TextWriter writer);
    private const string GenerateFromCurrentCommandName = "generate-from-current";

    private static readonly string[] CommandGroups =
    [
        "project",
        "projection",
        "queue",
        "bug",
        "run",
        "review",
        "interview",
        "clarify",
        "intake"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CommandHandler>> ImplementedCommands =
        new Dictionary<string, IReadOnlyDictionary<string, CommandHandler>>(StringComparer.Ordinal)
        {
            ["project"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["status"] = ProjectStatusCommand.Execute
            },
            ["projection"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["generate"] = ProjectionGenerateCommand.Generate,
                ["regenerate"] = ProjectionGenerateCommand.Regenerate
            },
            ["run"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["start"] = RunStartCommand.Execute,
                ["supervise"] = RunSuperviseCommand.Execute,
                ["submit"] = RunSubmitCommand.Execute,
                ["resubmit"] = RunResubmitCommand.Execute,
                ["rereview"] = RunRereviewCommand.Execute,
                ["resume"] = RunResumeCommand.Execute,
                ["log"] = RunLogCommand.Execute,
                ["implement"] = RunImplementCommand.Execute,
                ["fix"] = RunFixCommand.Execute
            },
            ["review"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["run"] = ReviewRunCommand.Execute,
                ["comment"] = ReviewCommentCommand.Execute,
                ["accept"] = ReviewAcceptCommand.Execute
            },
            ["interview"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["start"] = InterviewStartCommand.Execute,
                ["answer"] = InterviewAnswerCommand.Execute,
                ["resume"] = InterviewResumeCommand.Execute
            },
            ["clarify"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["open"] = ClarifyOpenCommand.Execute,
                ["list"] = ClarifyListCommand.Execute,
                ["answer"] = ClarifyAnswerCommand.Execute
            },
            ["queue"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["list"] = QueueListCommand.Execute,
                ["show"] = QueueShowCommand.Execute,
                ["next"] = QueueNextCommand.Execute,
                ["enqueue"] = QueueEnqueueCommand.Execute,
                ["dispatch"] = QueueDispatchCommand.Execute,
                ["transition"] = QueueTransitionCommand.Execute
            },
            ["bug"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["report"] = BugReportCommand.Execute,
                ["triage"] = BugTriageCommand.Execute,
                ["plan"] = BugExecutionCommand.Execute,
                ["intent-repair"] = BugIntentRepairCommand.Execute,
                ["intent-issue"] = BugIntentIssueCommand.Execute,
                ["intent-enqueue"] = BugIntentEnqueueCommand.Execute,
                ["intent-start"] = BugIntentStartCommand.Execute,
                ["intent-submit"] = BugIntentSubmitCommand.Execute,
                ["intent-review"] = BugIntentReviewCommand.Execute,
                ["intent-comment"] = BugIntentCommentCommand.Execute,
                ["implementation-repair"] = BugImplementationRepairCommand.Execute,
                ["implementation-issue"] = BugImplementationIssueCommand.Execute
            },
            ["intake"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["concept"] = IntakeConceptCommand.Execute,
                ["interview"] = IntakeInterviewCommand.Execute,
                ["compile"] = IntakeCompileCommand.Execute,
                ["foldin"] = IntakeFoldinCommand.Execute,
                ["patch"] = IntakePatchCommand.Execute,
                ["apply"] = IntakeApplyCommand.Execute,
                ["execution"] = IntakeExecutionCommand.Execute,
                ["advance"] = IntakeAdvanceCommand.Execute,
                ["activate"] = IntakeActivateCommand.Execute,
                ["issue"] = IntakeIssueCommand.Execute,
                ["enqueue"] = IntakeEnqueueCommand.Execute,
                ["autostart"] = IntakeAutostartCommand.Execute,
                ["launch"] = IntakeLaunchCommand.Execute,
                ["start"] = IntakeStartCommand.Execute
            }
        };

    public static int Execute(string[] args, CliContext context, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 0)
        {
            WriteHelp(writer);
            return 0;
        }

        if (string.Equals(args[0], GenerateFromCurrentCommandName, StringComparison.Ordinal))
        {
            return GenerateFromCurrentCommand.Execute(context, args[1..], writer);
        }

        if (args.Length == 1 && string.Equals(args[0], "run", StringComparison.Ordinal))
        {
            return RunCommand.Execute(context, [], writer);
        }

        if (args.Length < 2)
        {
            writer.WriteLine("A command group and subcommand are required.");
            WriteHelp(writer);
            return 1;
        }

        var group = args[0];
        var subcommand = args[1];

        if (!CommandGroups.Contains(group, StringComparer.Ordinal))
        {
            writer.WriteLine($"Unknown command group '{group}'.");
            WriteHelp(writer);
            return 1;
        }

        if (ImplementedCommands.TryGetValue(group, out var subcommands)
            && subcommands.TryGetValue(subcommand, out var handler))
        {
            return handler(context, args[2..], writer);
        }

        writer.WriteLine($"Command '{group} {subcommand}' is not yet implemented.");
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Available command groups:");
        foreach (var group in CommandGroups)
        {
            writer.WriteLine($"- {group}");
        }

        writer.WriteLine("Additional top-level commands:");
        writer.WriteLine($"- {GenerateFromCurrentCommandName}");
    }
}
