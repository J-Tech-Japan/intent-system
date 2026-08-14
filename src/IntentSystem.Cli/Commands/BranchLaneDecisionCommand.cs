using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class BranchLaneDecisionCommand
{
    internal static Func<DateTimeOffset> UtcNowFactory { get; set; } =
        static () => DateTimeOffset.UtcNow;

    internal static int ExecutePropose(CliContext context, string[] args, TextWriter writer)
        => Execute(context, args, writer, confirmation: false);

    internal static int ExecuteConfirm(CliContext context, string[] args, TextWriter writer)
        => Execute(context, args, writer, confirmation: true);

    private static int Execute(CliContext context, string[] args, TextWriter writer, bool confirmation)
    {
        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(confirmation
                ? "Usage: intent-cli automation branch-lane-confirm-record --execution-unit <unit> --actor <actor> --evidence <text> [--domain <name>] [--team <name>] [--actor-role operator|orchestration] [--recorded-at <iso>] [--write] [--format json|markdown]"
                : "Usage: intent-cli automation branch-lane-propose-record --execution-unit <unit> --actor <actor> --rationale <text> --evidence <text> [--domain <name>] [--team <name>] [--recorded-at <iso>] [--write] [--format json|markdown]");
            return 0;
        }

        var executionUnit = ReadRequired(args, "--execution-unit");
        var actor = ReadRequired(args, "--actor");
        var evidence = ReadRequired(args, "--evidence");
        var rationale = ReadOptional(args, "--rationale");
        var recordedAtText = ReadOptional(args, "--recorded-at") ?? ReadOptional(args, "--timestamp");
        var domain = ReadOptional(args, "--domain") ?? context.Config.Project.Domain;
        var team = ReadOptional(args, "--team");
        var requestedTeamMode = ReadOptional(args, "--team-mode") ?? ReadOptional(args, "--mode");
        var requestedActorRole = ReadOptional(args, "--actor-role");
        var format = ReadOptional(args, "--format") ?? "markdown";
        var write = HasFlag(args, "--write");

        if (executionUnit is null || actor is null || evidence is null)
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                Error = "execution-unit, actor, and evidence are required.",
            });
        }

        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var executionUnitError))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                ExecutionUnit = executionUnit,
                Error = executionUnitError,
            });
        }

        if (!confirmation && string.IsNullOrWhiteSpace(rationale))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = "propose",
                ExecutionUnit = executionUnit,
                Error = "rationale is required for a propose record.",
            });
        }

        if (!TryReadRecordedAt(recordedAtText, out var recordedAt, out var recordedAtError))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                ExecutionUnit = executionUnit,
                Error = recordedAtError,
            });
        }

        TeamModeResolution teamMode;
        try
        {
            teamMode = TeamModeStore.Resolve(context.RepoRoot, domain!, team);
        }
        catch (InvalidOperationException exception)
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                ExecutionUnit = executionUnit,
                Error = $"team-mode-unreadable: {exception.Message}",
            });
        }

        if (requestedTeamMode is not null
            && !TeamMode.IsKnown(requestedTeamMode))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                ExecutionUnit = executionUnit,
                Error = $"unknown team mode '{requestedTeamMode}'.",
            });
        }

        if (requestedTeamMode is not null
            && !string.Equals(requestedTeamMode, teamMode.Mode, StringComparison.Ordinal))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                ExecutionUnit = executionUnit,
                Error = $"requested team mode '{requestedTeamMode}' does not match the recorded mode '{teamMode.Mode}'.",
            });
        }

        var expectedActorRole = confirmation
            ? teamMode.IsAuthoringOnly ? "operator" : "orchestration"
            : "design";
        var actorRole = requestedActorRole ?? expectedActorRole;
        if (!string.Equals(actorRole, expectedActorRole, StringComparison.Ordinal))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                ExecutionUnit = executionUnit,
                Error = teamMode.IsAuthoringOnly
                    ? "authoring-only lane confirmation requires actor_role 'operator'; orchestration impersonation is refused."
                    : $"actor_role is '{actorRole}', expected '{expectedActorRole}'.",
            });
        }

        if (confirmation
            && teamMode.IsAuthoringOnly
            && string.Equals(actor, "orchestration", StringComparison.OrdinalIgnoreCase))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = "confirm",
                ExecutionUnit = executionUnit,
                Error = "authoring-only lane confirmation actor cannot be 'orchestration'; use a distinct operator identity.",
            });
        }

        if (!TryResolveSnapshot(context, executionUnit, args, out var snapshot, out var snapshotError))
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = confirmation ? "confirm" : "propose",
                ExecutionUnit = executionUnit,
                Error = snapshotError,
            });
        }

        var existingPropose = BranchLaneDecisionStore.ReadPropose(context.RepoRoot, executionUnit);
        var existingConfirm = BranchLaneDecisionStore.ReadConfirm(context.RepoRoot, executionUnit);

        if (confirmation && existingPropose.Record is null)
        {
            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = "confirm",
                ExecutionUnit = executionUnit,
                Error =
                    $"Cannot write a confirm record for '{executionUnit}': the propose record is missing; both propose and confirm records are required.",
            });
        }

        if (confirmation)
        {
            if (!BranchLaneDecisionStore.ValidateRecordMatches(existingPropose.Record!, snapshot, out var proposeSnapshotError)
                || !string.Equals(existingPropose.Record!.ActorRole, "design", StringComparison.Ordinal))
            {
                var roleError = string.Equals(existingPropose.Record!.ActorRole, "design", StringComparison.Ordinal)
                    ? string.Empty
                    : $"; actor_role is '{existingPropose.Record.ActorRole}', expected 'design'";
                return Emit(writer, format, new BranchLaneDecisionCommandResult
                {
                    Operation = "confirm",
                    ExecutionUnit = executionUnit,
                    Error = $"The propose record is invalid: {proposeSnapshotError}{roleError}",
                });
            }

            if (string.Equals(existingPropose.Record.Actor, actor, StringComparison.Ordinal))
            {
                return Emit(writer, format, new BranchLaneDecisionCommandResult
                {
                    Operation = "confirm",
                    ExecutionUnit = executionUnit,
                    Error = "The confirm actor must be independent from the propose actor.",
                });
            }

            if (recordedAt < existingPropose.Record.RecordedAt)
            {
                return Emit(writer, format, new BranchLaneDecisionCommandResult
                {
                    Operation = "confirm",
                    ExecutionUnit = executionUnit,
                    Error = "The confirm timestamp cannot precede the propose timestamp.",
                });
            }
        }

        if (confirmation && existingConfirm.Record is not null)
        {
            if (!ExistingRecordMatchesRequest(
                    existingConfirm.Record,
                    snapshot,
                    actor,
                    evidence,
                    rationale: null,
                    expectedRole: expectedActorRole,
                    out var existingError))
            {
                return Emit(writer, format, new BranchLaneDecisionCommandResult
                {
                    Operation = "confirm",
                    ExecutionUnit = executionUnit,
                    Error = $"A conflicting confirm record already exists: {existingError}",
                });
            }

            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = "confirm",
                ExecutionUnit = executionUnit,
                Applied = false,
                AlreadyRecorded = true,
                RecordPath = existingConfirm.Path,
                Record = existingConfirm.Record,
            });
        }

        if (!confirmation && existingPropose.Record is not null)
        {
            if (!ExistingRecordMatchesRequest(
                    existingPropose.Record,
                    snapshot,
                    actor,
                    evidence,
                    rationale,
                    expectedRole: "design",
                    out var existingError))
            {
                return Emit(writer, format, new BranchLaneDecisionCommandResult
                {
                    Operation = "propose",
                    ExecutionUnit = executionUnit,
                    Error = $"A conflicting propose record already exists: {existingError}",
                });
            }

            return Emit(writer, format, new BranchLaneDecisionCommandResult
            {
                Operation = "propose",
                ExecutionUnit = executionUnit,
                Applied = false,
                AlreadyRecorded = true,
                RecordPath = existingPropose.Path,
                Record = existingPropose.Record,
            });
        }

        var fingerprint = BranchLaneDecisionStore.ComputeFingerprint(snapshot);
        BranchLaneDecisionRecord record = confirmation
            ? new BranchLaneConfirmRecord
            {
                RecordKind = BranchLaneConfirmRecord.Kind,
                ExecutionUnit = executionUnit,
                LaneId = snapshot.LaneId,
                StartBranch = snapshot.StartBranch,
                PrBaseBranch = snapshot.PrBaseBranch,
                LandingMode = snapshot.LandingMode,
                DefinitionRevision = snapshot.DefinitionRevision,
                Actor = actor,
                ActorRole = actorRole,
                RecordedAt = recordedAt,
                Evidence = evidence,
                Fingerprint = fingerprint,
                TeamMode = teamMode.IsAuthoringOnly ? TeamMode.AuthoringOnly : null,
            }
            : new BranchLaneProposeRecord
            {
                RecordKind = BranchLaneProposeRecord.Kind,
                ExecutionUnit = executionUnit,
                LaneId = snapshot.LaneId,
                StartBranch = snapshot.StartBranch,
                PrBaseBranch = snapshot.PrBaseBranch,
                LandingMode = snapshot.LandingMode,
                DefinitionRevision = snapshot.DefinitionRevision,
                Actor = actor,
                ActorRole = "design",
                RecordedAt = recordedAt,
                Evidence = evidence,
                Fingerprint = fingerprint,
                Rationale = rationale!,
                TeamMode = teamMode.IsAuthoringOnly ? TeamMode.AuthoringOnly : null,
            };

        var path = BranchLaneDecisionStore.ResolveRelativePath(executionUnit, confirmation);
        if (write)
        {
            var writeResult = BranchLaneDecisionStore.Write(
                context.RepoRoot,
                executionUnit,
                record,
                confirmation);
            if (!writeResult.Succeeded)
            {
                return Emit(writer, format, new BranchLaneDecisionCommandResult
                {
                    Operation = confirmation ? "confirm" : "propose",
                    ExecutionUnit = executionUnit,
                    Error = writeResult.Error,
                });
            }
        }

        return Emit(writer, format, new BranchLaneDecisionCommandResult
        {
            Operation = confirmation ? "confirm" : "propose",
            ExecutionUnit = executionUnit,
            Applied = write,
            RecordPath = path,
            Record = record,
            PreviewStatus = write ? "written" : "preview",
        });
    }

    private static bool TryResolveSnapshot(
        CliContext context,
        string executionUnit,
        string[] args,
        out BranchRoutingSnapshot snapshot,
        out string error)
    {
        snapshot = default!;
        error = string.Empty;

        var packetPath = Path.Combine(
            context.RepoRoot,
            ".intent-cli",
            "issues",
            executionUnit,
            "packet.yaml");

        try
        {
            if (File.Exists(packetPath))
            {
                var packetYaml = File.ReadAllText(packetPath);
                var parsed = PacketYamlDocument.TryParse(packetYaml, out var packet, out var packetError);
                if (parsed)
                {
                    var packetSnapshot = BranchLaneResolver.TryReadSnapshot(packet!.Fields);
                    if (packetSnapshot is not null)
                    {
                        snapshot = packetSnapshot;
                        return ValidateOptionalFacts(snapshot, args, out error);
                    }
                }
                else
                {
                    error = "could not parse packet.yaml: " + packetError;
                    return false;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            error = $"could not read lane snapshot from packet.yaml: {exception.Message}";
            return false;
        }

        if (TryReadRequiredFacts(args, out snapshot, out error))
        {
            return true;
        }

        error =
            $"A lane snapshot is required for '{executionUnit}'. Supply a lane-declaring packet or --lane-id, --start-branch, --pr-base-branch, --landing-mode, and --definition-revision.";
        return false;
    }

    private static bool ExistingRecordMatchesRequest(
        BranchLaneDecisionRecord record,
        BranchRoutingSnapshot snapshot,
        string actor,
        string evidence,
        string? rationale,
        string expectedRole,
        out string error)
    {
        var problems = new List<string>();
        if (!BranchLaneDecisionStore.ValidateRecordMatches(record, snapshot, out var snapshotError))
        {
            problems.Add(snapshotError);
        }
        if (!string.Equals(record.Actor, actor, StringComparison.Ordinal))
        {
            problems.Add($"actor is '{record.Actor}', requested '{actor}'");
        }
        if (!string.Equals(record.ActorRole, expectedRole, StringComparison.Ordinal))
        {
            problems.Add($"actor_role is '{record.ActorRole}', expected '{expectedRole}'");
        }
        if (!string.Equals(record.Evidence, evidence, StringComparison.Ordinal))
        {
            problems.Add("evidence differs from the durable record");
        }
        if (record is BranchLaneProposeRecord propose
            && !string.Equals(propose.Rationale, rationale, StringComparison.Ordinal))
        {
            problems.Add("rationale differs from the durable record");
        }

        error = string.Join("; ", problems);
        return problems.Count == 0;
    }

    private static bool TryReadRequiredFacts(
        string[] args,
        out BranchRoutingSnapshot snapshot,
        out string error)
    {
        snapshot = default!;
        error = string.Empty;

        var laneId = ReadOptional(args, "--lane-id") ?? ReadOptional(args, "--lane");
        var revision = ReadOptional(args, "--definition-revision") ??
                       ReadOptional(args, "--lane-definition-revision");
        var startBranch = ReadOptional(args, "--start-branch");
        var prBaseBranch = ReadOptional(args, "--pr-base-branch");
        var landingMode = ReadOptional(args, "--landing-mode");

        if (string.IsNullOrWhiteSpace(laneId) ||
            string.IsNullOrWhiteSpace(revision) ||
            string.IsNullOrWhiteSpace(startBranch) ||
            string.IsNullOrWhiteSpace(prBaseBranch) ||
            string.IsNullOrWhiteSpace(landingMode))
        {
            error = "The lane snapshot is incomplete.";
            return false;
        }

        snapshot = new BranchRoutingSnapshot
        {
            LaneId = laneId,
            DefinitionRevision = revision,
            StartBranch = startBranch,
            PrBaseBranch = prBaseBranch,
            LandingMode = landingMode,
        };
        return true;
    }

    private static bool ValidateOptionalFacts(
        BranchRoutingSnapshot snapshot,
        string[] args,
        out string error)
    {
        error = string.Empty;
        var supplied = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lane_id"] = ReadOptional(args, "--lane-id") ?? ReadOptional(args, "--lane") ?? string.Empty,
            ["definition_revision"] = ReadOptional(args, "--definition-revision") ??
                                      ReadOptional(args, "--lane-definition-revision") ?? string.Empty,
            ["start_branch"] = ReadOptional(args, "--start-branch") ?? string.Empty,
            ["pr_base_branch"] = ReadOptional(args, "--pr-base-branch") ?? string.Empty,
            ["landing_mode"] = ReadOptional(args, "--landing-mode") ?? string.Empty,
        };
        var actual = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lane_id"] = snapshot.LaneId,
            ["definition_revision"] = snapshot.DefinitionRevision,
            ["start_branch"] = snapshot.StartBranch,
            ["pr_base_branch"] = snapshot.PrBaseBranch,
            ["landing_mode"] = snapshot.LandingMode,
        };

        foreach (var pair in supplied.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            if (!string.Equals(pair.Value, actual[pair.Key], StringComparison.Ordinal))
            {
                error =
                    $"The supplied {pair.Key} '{pair.Value}' does not match the packet snapshot '{actual[pair.Key]}'.";
                return false;
            }
        }

        return true;
    }

    private static bool TryReadRecordedAt(
        string? value,
        out DateTimeOffset recordedAt,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            recordedAt = UtcNowFactory();
            error = string.Empty;
            return true;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out recordedAt))
        {
            error = string.Empty;
            return true;
        }

        error = $"recorded-at must be an ISO-8601 timestamp: '{value}'.";
        return false;
    }

    private static string? ReadRequired(string[] args, string name)
    {
        var value = ReadOptional(args, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadOptional(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        return args[index + 1];
    }

    private static bool HasFlag(string[] args, string name)
        => args.Contains(name, StringComparer.Ordinal);

    private static int Emit(
        TextWriter writer,
        string format,
        BranchLaneDecisionCommandResult result)
    {
        var normalizedFormat = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            ? "json"
            : "markdown";

        if (normalizedFormat == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                }));
        }
        else if (result.Error is not null)
        {
            writer.WriteLine($"error: {result.Error}");
        }
        else
        {
            writer.WriteLine(
                $"{result.Operation} {result.ExecutionUnit}: {result.PreviewStatus ?? (result.AlreadyRecorded ? "already recorded" : "ok")}.");
            if (result.RecordPath is not null)
            {
                writer.WriteLine($"record: {result.RecordPath}");
            }
        }

        return result.Error is null ? 0 : 1;
    }
}

internal sealed class BranchLaneDecisionCommandResult
{
    public string Operation { get; init; } = string.Empty;
    public string? ExecutionUnit { get; init; }
    public bool Applied { get; init; }
    public bool AlreadyRecorded { get; init; }
    public string? PreviewStatus { get; init; }
    public string? RecordPath { get; init; }
    // Keep the result's wire shape polymorphic without asking
    // System.Text.Json to serialize the abstract base type.
    public object? Record { get; init; }
    public string? Error { get; init; }
}
