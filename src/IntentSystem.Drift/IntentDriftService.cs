using IntentSystem.Drift.Models;
using IntentSystem.Projection;
using IntentSystem.Projection.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Drift;

public static class IntentDriftService
{
    private const string QueueSchemaVersion = "1";
    private const string TransitionActor = "intent-drift-service";
    private const string CorrectiveEventName = "corrective-enqueued";
    private const string DefaultTargetRepo = "J-Tech-Japan/intent-system";
    private const string DefaultTargetPath = ".";
    private const string DefaultClarificationReturnPath = "intents/intent-cli/clarifications/open.md";

    public static DriftProcessingResult Process(
        QueueState queueState,
        IReadOnlyList<ChangedCanonicalRef> changedCanonicalRefs,
        string repoRoot,
        string queueStatePath,
        string runLogPath,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(queueState);
        ArgumentNullException.ThrowIfNull(changedCanonicalRefs);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueStatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runLogPath);

        var items = new List<DriftClassificationItem>();
        var appendedEvents = new List<RunEvent>();
        var updatedItems = queueState.Items.ToList();

        foreach (var queueItem in queueState.Items)
        {
            if (queueItem.State != QueueItemState.Completed)
            {
                continue;
            }

            var matchingRefs = changedCanonicalRefs
                .Where(changedRef => changedRef.AffectedExecutionUnits.Contains(queueItem.ExecutionUnit, StringComparer.Ordinal))
                .ToArray();

            if (matchingRefs.Length == 0)
            {
                continue;
            }

            var classification = matchingRefs
                .Select(changedRef => changedRef.Classification)
                .OrderByDescending(GetClassificationSeverity)
                .First();

            string? correctiveExecutionUnit = null;
            if (classification == DriftClassification.AcceptedContractBreaking)
            {
                correctiveExecutionUnit = CreateCorrectiveExecutionUnit(queueItem.ExecutionUnit, updatedItems);
                var packet = GenerateCorrectivePacket(queueItem, correctiveExecutionUnit, matchingRefs);
                ProjectionArtifactWriter.Write(packet, repoRoot, overwrite: false);

                updatedItems.Add(CreateCorrectiveQueueItem(queueItem, packet, correctiveExecutionUnit));
                appendedEvents.Add(new RunEvent
                {
                    Ts = timestamp,
                    ExecutionUnit = correctiveExecutionUnit,
                    Event = CorrectiveEventName,
                    By = TransitionActor,
                    Reason = string.Join("; ", matchingRefs.Select(changedRef => $"{changedRef.CanonicalRef}: {changedRef.DriftSummary}"))
                });
            }

            items.Add(new DriftClassificationItem
            {
                ExecutionUnit = queueItem.ExecutionUnit,
                Classification = classification,
                ChangedCanonicalRefs = matchingRefs.Select(changedRef => changedRef.CanonicalRef).ToArray(),
                CorrectiveExecutionUnit = correctiveExecutionUnit
            });
        }

        var updatedQueueState = queueState with
        {
            SchemaVersion = QueueSchemaVersion,
            UpdatedAt = appendedEvents.Count == 0 ? queueState.UpdatedAt : timestamp,
            Items = updatedItems
        };

        if (appendedEvents.Count > 0)
        {
            File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedQueueState));

            var runLogDirectory = Path.GetDirectoryName(runLogPath)
                ?? throw new InvalidOperationException("Run log path did not contain a directory.");
            Directory.CreateDirectory(runLogDirectory);
            foreach (var runEvent in appendedEvents)
            {
                File.AppendAllText(runLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
            }
        }

        return new DriftProcessingResult
        {
            Report = new DriftClassificationReport
            {
                Items = items
            },
            UpdatedQueueState = updatedQueueState,
            AppendedEvents = appendedEvents
        };
    }

    private static int GetClassificationSeverity(DriftClassification classification)
    {
        return classification switch
        {
            DriftClassification.AcceptedContractBreaking => 3,
            DriftClassification.StateOnly => 2,
            DriftClassification.FutureOnly => 1,
            DriftClassification.DocumentationOnly => 0,
            _ => throw new InvalidOperationException($"Unsupported drift classification '{classification}'.")
        };
    }

    private static string CreateCorrectiveExecutionUnit(
        string sourceExecutionUnit,
        IReadOnlyList<QueueItem> queueItems)
    {
        var existingUnits = queueItems
            .Select(queueItem => queueItem.ExecutionUnit)
            .ToHashSet(StringComparer.Ordinal);

        var candidate = $"{sourceExecutionUnit}-corrective";
        if (!existingUnits.Contains(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (true)
        {
            candidate = $"{sourceExecutionUnit}-corrective-{suffix}";
            if (!existingUnits.Contains(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static GeneratedPacket GenerateCorrectivePacket(
        QueueItem queueItem,
        string correctiveExecutionUnit,
        IReadOnlyList<ChangedCanonicalRef> matchingRefs)
    {
        var implementationPacket = new ImplementationIssuePacket
        {
            IssueTitle = $"[{queueItem.ExecutionUnit}] Corrective drift follow-up",
            IssueKind = IssueKind.BoundaryFix,
            SourceExecutionUnit = correctiveExecutionUnit,
            Goal = $"Repair accepted execution unit '{queueItem.ExecutionUnit}' so it matches the current canonical source updates.",
            InScope =
            [
                $"repair contract drift for accepted execution unit '{queueItem.ExecutionUnit}'",
                "update affected implementation or artifact boundaries to match current canonical refs"
            ],
            OutOfScope =
            [
                "parent Intent trigger automation",
                "GitHub issue auto-creation",
                "accepted issue closeout",
                "node_modules/",
                ".takt/runs/",
                "runtime trace / generated cache / temporary report"
            ],
            TargetRepo = queueItem.LinkedIssue?.Repo ?? DefaultTargetRepo,
            TargetPath = DefaultTargetPath,
            TargetPart = $"corrective follow-up for {queueItem.ExecutionUnit}",
            Dependencies = [queueItem.ExecutionUnit],
            TechnicalBaseline =
            [
                "C# / .NET",
                ".NET 10.0.100+ baseline",
                "dnx / dotnet tool exec",
                "do not switch to Node or TypeScript toolchain"
            ],
            ProjectLocalGuide =
            [
                "AGENTS.md",
                "CLAUDE.md"
            ],
            IntentBaseline = matchingRefs
                .Select(changedRef => $"drift source '{changedRef.CanonicalRef}' caused: {changedRef.DriftSummary}")
                .ToArray(),
            IntentReferences = matchingRefs
                .Select(changedRef => changedRef.CanonicalRef)
                .ToArray(),
            RulesAndSpecs =
            [
                "intents/rules/intent-diff-and-corrective-issues.md",
                "intents/rules/nonblocking-agent-loop.md",
                "intents/rules/issue-template-and-review-context.md",
                "intents/intent-cli/specs/08-config-and-run-model.md",
                "intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md"
            ],
            AcceptanceCriteria =
            [
                $"accepted execution unit '{queueItem.ExecutionUnit}' no longer drifts from the updated canonical refs",
                "corrective implementation keeps queue and run artifact baselines intact"
            ],
            VerificationEvidence =
            [
                "contract-reviewed",
                "tests-passing",
                "acceptance-criteria-checked"
            ],
            ReviewMode = "deterministic-review",
            CompletionAction = "wait-for-deterministic-review",
            LandingPolicy = "merge-after-review"
        };

        var reviewContextPacket = new ReviewContextPacket
        {
            SourceExecutionUnit = correctiveExecutionUnit,
            ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md",
            IntentReferences = matchingRefs
                .Select(changedRef => changedRef.CanonicalRef)
                .ToArray(),
            RulesAndSpecs = implementationPacket.RulesAndSpecs,
            AcceptanceCriteria = implementationPacket.AcceptanceCriteria,
            DeterministicReviewChecks =
            [
                "drift service が parent Intent trigger 自動化や accepted issue closeout の責務へ広がっていない",
                "diff classification が current four-way baseline に従っている",
                "corrective issue packet stub は `accepted-contract-breaking` のときだけ生成されている",
                "queue enqueue と `runs.jsonl` append が current queue baseline を崩していない"
            ],
            ClarificationReturnPath = queueItem.ClarificationReturnPath
        };

        return PacketGenerator.Generate(implementationPacket, reviewContextPacket);
    }

    private static QueueItem CreateCorrectiveQueueItem(
        QueueItem sourceItem,
        GeneratedPacket packet,
        string correctiveExecutionUnit)
    {
        return new QueueItem
        {
            ExecutionUnit = correctiveExecutionUnit,
            Title = $"[{sourceItem.ExecutionUnit}] Corrective drift follow-up",
            State = QueueItemState.Queued,
            Dependencies = [sourceItem.ExecutionUnit],
            BlockedBy = [],
            ClarificationReturnPath = sourceItem.ClarificationReturnPath ?? DefaultClarificationReturnPath,
            PacketPaths = new PacketPaths
            {
                Implementation = packet.Paths.Implementation,
                ReviewContext = packet.Paths.ReviewContext,
                Yaml = packet.Paths.Yaml
            },
            LinkedIssue = null,
            WorkerRole = sourceItem.WorkerRole,
            ReviewRole = sourceItem.ReviewRole,
            Priority = "high"
        };
    }
}
