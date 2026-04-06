using IntentSystem.Supervisor;

namespace IntentSystem.Cli.Commands;

internal static class IntakeEnqueueCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake enqueue command requires a domain.");
            return 1;
        }

        var domain = args[0].Trim();
        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        try
        {
            var units = LoadExecutionUnits(context.RepoRoot, domain);
            if (units.Count == 0)
            {
                writer.WriteLine($"No generated execution units were found for domain '{domain}'.");
                return 1;
            }

            var candidateQueueItems = ResolveQueueItems(context, units);
            var result = EnqueueQueueItems(context, domain, queueState, candidateQueueItems);
            IntakeEnqueueRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IReadOnlyList<string> LoadExecutionUnits(string repoRoot, string domain)
    {
        var executionArtifactPath = Path.Combine(
            repoRoot,
            IntakeExecutionArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(executionArtifactPath))
        {
            throw new InvalidOperationException($"Intake execution artifact was not found at {executionArtifactPath}");
        }

        var request = IntakeExecutionArtifactMarkdown.Deserialize(File.ReadAllText(executionArtifactPath));
        if (!string.Equals(request.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake execution artifact domain '{request.Domain}' does not match requested domain '{domain}'.");
        }

        return request.ProposedExecutionUnits
            .Select(unit => unit.ExecutionUnitId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(unit => unit, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<CandidateQueueItem> ResolveQueueItems(
        CliContext context,
        IReadOnlyList<string> executionUnits)
    {
        var results = new List<CandidateQueueItem>(executionUnits.Count);

        foreach (var executionUnit in executionUnits)
        {
            var packetPath = QueueEnqueueCommand.ResolvePacketPath(context, executionUnit);
            if (!File.Exists(packetPath))
            {
                throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
            }

            var packet = QueueEnqueueCommand.ReadPacket(File.ReadAllText(packetPath), executionUnit);
            var packetPaths = QueueEnqueueCommand.ResolvePacketPaths(context.RepoRoot, executionUnit);
            var queueItem = QueueEnqueueCommand.CreateQueueItem(context, packet, packetPaths);

            results.Add(new CandidateQueueItem
            {
                ExecutionUnit = executionUnit,
                QueueItem = queueItem
            });
        }

        return results;
    }

    private static IntakeEnqueueResult EnqueueQueueItems(
        CliContext context,
        string domain,
        IntentSystem.Supervisor.Models.QueueState initialQueueState,
        IReadOnlyList<CandidateQueueItem> candidates)
    {
        var currentQueueState = initialQueueState;
        var enqueuedExecutionUnits = new List<string>();
        var packetPaths = new List<string>();
        var skippedUnits = new List<string>();
        var events = new List<IntentSystem.Supervisor.Models.RunEvent>();

        foreach (var candidate in candidates)
        {
            var enqueueResult = QueueManager.Enqueue(
                currentQueueState,
                candidate.QueueItem,
                "intent-cli",
                QueueEnqueueCommand.TimestampFactory());

            currentQueueState = enqueueResult.UpdatedState;

            if (!enqueueResult.WasEnqueued)
            {
                skippedUnits.Add(candidate.ExecutionUnit);
                continue;
            }

            enqueuedExecutionUnits.Add(candidate.ExecutionUnit);
            packetPaths.Add(candidate.QueueItem.PacketPaths.Implementation);
            packetPaths.Add(candidate.QueueItem.PacketPaths.ReviewContext);
            packetPaths.Add(candidate.QueueItem.PacketPaths.Yaml);
            if (enqueueResult.Event is not null)
            {
                events.Add(enqueueResult.Event);
            }
        }

        if (events.Count > 0)
        {
            Persist(context, currentQueueState, events);
        }

        return new IntakeEnqueueResult
        {
            Domain = domain,
            EnqueuedExecutionUnits = enqueuedExecutionUnits,
            PacketPaths = packetPaths,
            SkippedUnits = skippedUnits
        };
    }

    private static void Persist(
        CliContext context,
        IntentSystem.Supervisor.Models.QueueState queueState,
        IReadOnlyList<IntentSystem.Supervisor.Models.RunEvent> events)
    {
        var queueStatePath = context.GetQueueStatePath();
        File.WriteAllText(
            queueStatePath,
            IntentSystem.Supervisor.Serialization.QueueStateSerializer.Serialize(queueState));

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);

        var serializedEvents = string.Join(
            Environment.NewLine,
            events.Select(IntentSystem.Supervisor.Serialization.RunLogSerializer.SerializeLine));
        File.AppendAllText(runLogPath, serializedEvents + Environment.NewLine);
    }

    private sealed record CandidateQueueItem
    {
        public required string ExecutionUnit { get; init; }

        public required IntentSystem.Supervisor.Models.QueueItem QueueItem { get; init; }
    }
}
