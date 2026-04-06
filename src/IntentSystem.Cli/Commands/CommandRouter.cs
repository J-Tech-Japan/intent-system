namespace IntentSystem.Cli.Commands;

internal static class CommandRouter
{
    private delegate int CommandHandler(CliContext context, string[] args, TextWriter writer);

    private static readonly string[] CommandGroups =
    [
        "project",
        "projection",
        "queue",
        "run",
        "review",
        "interview",
        "clarify",
        "workflow",
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
                ["submit"] = RunSubmitCommand.Execute,
                ["resubmit"] = RunResubmitCommand.Execute,
                ["rereview"] = RunRereviewCommand.Execute,
                ["resume"] = RunResumeCommand.Execute,
                ["log"] = RunLogCommand.Execute,
                ["implement"] = RunImplementCommand.Execute,
                ["fix"] = RunFixCommand.Execute
            },
            ["workflow"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["render"] = WorkflowRenderCommand.Execute,
                ["run"] = WorkflowRunCommand.Execute,
                ["status"] = WorkflowStatusCommand.Execute
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
                ["dispatch"] = QueueDispatchCommand.Execute,
                ["transition"] = QueueTransitionCommand.Execute
            },
            ["intake"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["concept"] = IntakeConceptCommand.Execute,
                ["compile"] = IntakeCompileCommand.Execute,
                ["foldin"] = IntakeFoldinCommand.Execute,
                ["patch"] = IntakePatchCommand.Execute,
                ["apply"] = IntakeApplyCommand.Execute,
                ["execution"] = IntakeExecutionCommand.Execute,
                ["autostart"] = IntakeAutostartCommand.Execute
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
    }
}
