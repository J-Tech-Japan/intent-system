using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G796: event-kind routing and the mechanical Steward boundary. These tests
/// stay transport-neutral so the routing decision can be audited without
/// launching a process or changing a topology file.
/// </summary>
public sealed class NotifyRoutingG796Tests
{
    private readonly ITestOutputHelper output;

    public NotifyRoutingG796Tests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData("completion", "architect", "steward")]
    [InlineData("transition", "architect", "steward")]
    [InlineData("acknowledgement", "architect", "steward")]
    [InlineData("escalation", "steward", "architect")]
    [InlineData("question", "architect", "architect")]
    [InlineData("question", "review", "reviewer")]
    [InlineData("blocked", "steward", "architect")]
    public void EveryEventKindUsesKindSpecificStewardRoute_G796(
        string eventKind,
        string currentTarget,
        string expectedTarget)
    {
        var routed = NotifyEventKindRouting.ResolveTarget(
            eventKind,
            currentTarget,
            stewardRecorded: true);

        Assert.Equal(expectedTarget, routed);
        output.WriteLine($"G796 AC1 event_kind={eventKind}; steward_recorded=true; current_target={currentTarget}; routed_to={routed}; accepted=true");
    }

    [Fact]
    public void RulingDigestIsComputedOverOpaquePayloadAndOriginIsCanonical_G796()
    {
        const string payload = "architect ruling: retain the recorded contract\nwith opaque bytes";
        Assert.True(NotifyRuling.TryCreate(
            payload,
            "design",
            suppliedDigest: null,
            out var ruling,
            out var error), error);
        Assert.NotNull(ruling);
        Assert.Equal(
            NotifyRulingRelay.ComputeDigest(payload),
            ruling!.Digest);
        Assert.Equal("architect", ruling.Origin);
        Assert.Equal(
            Encoding.UTF8.GetBytes(payload),
            ruling.PayloadBytes);
        output.WriteLine($"G796 AC2 payload_bytes={ruling.PayloadBytes.Count}; digest={ruling.Digest}; origin={ruling.Origin}; verifies={ruling.Verifies()}");
    }

    [Fact]
    public void StewardRelayPreservesPayloadAndDigestWhileAllowingEnvelopeFields_G796()
    {
        const string payload = "opaque-architect-ruling";
        Assert.True(NotifyRuling.TryCreate(payload, "architect", null, out var ruling, out var createError), createError);
        Assert.NotNull(ruling);

        var envelopeFields = new Dictionary<string, string>
        {
            ["relay_id"] = "steward-relay-1",
        };
        Assert.True(
            NotifyRulingRelay.TryRelay(ruling!, payload, envelopeFields, out var relay),
            relay.Summary);
        Assert.True(relay.Accepted);
        Assert.Equal(payload, relay.Ruling!.Payload);
        Assert.True(
            Encoding.UTF8.GetBytes(payload).SequenceEqual(relay.Ruling.PayloadBytes));
        Assert.Equal(ruling.Digest, relay.Ruling.Digest);
        Assert.Equal("architect", relay.Ruling.Origin);
        Assert.Equal("steward-relay-1", relay.Envelope!.Fields["relay_id"]);
        Assert.True(relay.Ruling.Verifies());
        output.WriteLine($"G796 AC3/AC4 accepted={relay.Accepted}; payload_bytes_equal={Encoding.UTF8.GetBytes(payload).SequenceEqual(relay.Ruling.PayloadBytes)}; digest_unchanged={ruling.Digest == relay.Ruling.Digest}; origin={relay.Ruling.Origin}; envelope.relay_id={relay.Envelope.Fields["relay_id"]}; verifies={relay.Ruling.Verifies()}");
    }

    [Fact]
    public void OneByteChangedRulingPayloadIsRefusedWithDigestMismatch_G796()
    {
        const string payload = "opaque-architect-ruling";
        Assert.True(NotifyRuling.TryCreate(payload, "architect", null, out var ruling, out var createError), createError);
        var changed = "opaque-architect-rulinG";

        Assert.False(NotifyRulingRelay.TryRelay(ruling!, changed, new Dictionary<string, string>(), out var relay));
        Assert.False(relay.Accepted);
        Assert.Equal("ruling-digest-mismatch", relay.Cause);
        Assert.Contains("digest mismatch", relay.Summary!, StringComparison.OrdinalIgnoreCase);
        output.WriteLine($"G796 AC5 accepted={relay.Accepted}; cause={relay.Cause}; summary={relay.Summary}");
    }

    [Theory]
    [InlineData("question", "architect", "Architect")]
    [InlineData("question", "reviewer", "Reviewer")]
    [InlineData("escalation", "architect", "Architect")]
    [InlineData("blocked", "architect", "Architect")]
    public void StewardJudgementWithoutDownstreamDelegationIsRefused_G796(
        string eventKind,
        string target,
        string requiredTarget)
    {
        Assert.False(NotifyRulingRelay.TryValidateStewardAnswer(
            "steward",
            eventKind,
            target,
            downstreamDelegationReference: null,
            out var error));
        Assert.Contains(requiredTarget, error, StringComparison.Ordinal);
        output.WriteLine($"G796 AC6 event_kind={eventKind}; from=steward; target={target}; accepted=false; required_downstream_target={requiredTarget}; error={error}");
    }

    [Theory]
    [InlineData("completion")]
    [InlineData("transition")]
    [InlineData("acknowledgement")]
    public void StewardMayAnswerRoutineEventsWithoutDelegation_G796(string eventKind)
    {
        Assert.True(NotifyRulingRelay.TryValidateStewardAnswer(
            "steward",
            eventKind,
            "steward",
            downstreamDelegationReference: null,
            out var error), error);
        output.WriteLine($"G796 AC7 event_kind={eventKind}; from=steward; downstream_reference=<none>; accepted=true");
    }

    [Fact]
    public void DownstreamCheckUsesTheG788RecognizerAndFieldOrder_G796()
    {
        var pattern = new Regex(
            "^G796$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Assert.True(NotifyRulingRelay.HasDownstreamDelegationEvidence(
            taskId: "child-task",
            objective: "coordinate G796 downstream",
            inputs: ["ordinary input"],
            pattern,
            expectedExecutionUnit: "G796"));
        Assert.False(NotifyRulingRelay.HasDownstreamDelegationEvidence(
            taskId: "G795-child",
            objective: "coordinate G795 downstream",
            inputs: ["ordinary input"],
            pattern,
            expectedExecutionUnit: "G796"));
        output.WriteLine("G796 AC8 recognizer=NotifyDelegationExecutionEvidence.ExtractExecutionUnitToken; objective-fallback=true; earlier-token-wins=true; second-recognizer=false");
    }

    [Fact]
    public void ProductionStewardGateRejectsFabricatedReference_G796()
    {
        var root = NewEvidenceRoot();
        try
        {
            Assert.True(NotifyRuling.TryCreate(
                "opaque upstream ruling",
                "architect",
                null,
                out var ruling,
                out var createError), createError);
            var parent = WriteUpstreamParent(root, ruling);
            var context = new CliContext
            {
                RepoRoot = root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
                },
            };
            using var writer = new StringWriter();
            var exit = NotifyCommand.ExecuteReport(
                context,
                [
                    "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--from", "steward", "--to", "architect", "--task-id", parent.TaskId,
                    "--status", "question", "--artifact", "answer.txt", "--summary", "fabricated",
                    "--event-kind", "question", "--downstream-delegation-reference", "fabricated-G796-proof",
                    "--routing-root", root, "--report-root", root, "--dry-run", "--format", "json",
                ],
                writer);

            Assert.Equal(1, exit);
            using var document = JsonDocument.Parse(writer.ToString());
            var result = document.RootElement;
            Assert.Equal("steward-boundary-refused", result.GetProperty("cause").GetString());
            Assert.Contains("did not resolve", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
            output.WriteLine($"G796 AC6/AC8 fabricated_reference={result.GetProperty("downstream_delegation_reference").GetString()}; exit={exit}; cause={result.GetProperty("cause").GetString()}; summary={result.GetProperty("summary").GetString()}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }

    [Fact]
    public void DownstreamReferenceRejectsPrefixOfRealCarrierValue_G796()
    {
        AssertPendingReferenceRejected(
            requestedReference: "G796-child",
            recordedReference: "G796-child-real",
            negativeClass: "prefix");
    }

    [Fact]
    public void DownstreamReferenceRejectsSuffixOfRealCarrierValue_G796()
    {
        AssertPendingReferenceRejected(
            requestedReference: "child-real",
            recordedReference: "G796-child-real",
            negativeClass: "suffix");
    }

    [Fact]
    public void DownstreamReferenceRejectsReferenceFromDifferentTeamOrDomain_G796()
    {
        var root = NewEvidenceRoot();
        try
        {
            var parent = WriteUpstreamParent(root);
            var foreignTeamReference = "G796-child-foreign-team";
            Assert.True(
                NotifyEventWriter.TryResolveWritePath(
                    root,
                    parent.Domain,
                    "other-team",
                    out var foreignEventPath,
                    out var foreignEventError),
                foreignEventError);
            NotifyEventWriter.Append(foreignEventPath, new NotifyDesignEvent
            {
                Timestamp = parent.DispatchedAt.AddSeconds(1),
                Team = "other-team",
                Kind = "question",
                Unit = foreignTeamReference,
                Summary = "foreign-team child evidence",
                Artifact = "child.txt",
            });

            var foreignDomainReference = "G796-child-foreign-domain";
            var foreignDomainChild = parent with
            {
                Domain = "other-domain",
                TaskId = foreignDomainReference,
                DispatchedAt = parent.DispatchedAt.AddSeconds(1),
            };
            Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, foreignDomainChild).Written);

            Assert.False(
                NotifyDelegationExecutionEvidence.TryResolveDownstreamReference(
                    root,
                    parent,
                    foreignTeamReference,
                    out _,
                    out var teamError));
            Assert.Contains("did not resolve", teamError, StringComparison.OrdinalIgnoreCase);
            Assert.False(
                NotifyDelegationExecutionEvidence.TryResolveDownstreamReference(
                    root,
                    parent,
                    foreignDomainReference,
                    out _,
                    out var domainError));
            Assert.Contains("did not resolve", domainError, StringComparison.OrdinalIgnoreCase);
            output.WriteLine(
                $"G796 AC6/AC8 negative_class=foreign-team-or-domain; team_reference={foreignTeamReference}; team_accepted=false; team_error={teamError}; domain_reference={foreignDomainReference}; domain_accepted=false; domain_error={domainError}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }

    [Fact]
    public void DownstreamReferenceRejectsFabricatedIdentifierWithExactCarrierComparison_G796()
    {
        AssertPendingReferenceRejected(
            requestedReference: "fabricated-G796-proof",
            recordedReference: "G796-child-real",
            negativeClass: "fabricated");
    }

    [Fact]
    public void DownstreamReferenceMatchesQueueCarrierWithExactExecutionUnitValue_G796()
    {
        var root = NewEvidenceRoot();
        try
        {
            var parent = WriteUpstreamParent(root);
            var queueState = new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = parent.DispatchedAt.AddSeconds(1),
                Items =
                [
                    new QueueItem
                    {
                        ExecutionUnit = "G796",
                        Title = "G796 queue transition",
                        State = QueueItemState.Active,
                        Dependencies = [],
                        BlockedBy = [],
                        ClarificationReturnPath = string.Empty,
                        PacketPaths = new PacketPaths
                        {
                            Yaml = ".intent-cli/issues/G796/packet.yaml",
                            Implementation = ".intent-cli/issues/G796/implementation.md",
                            ReviewContext = ".intent-cli/issues/G796/review-context.md",
                        },
                        WorkerRole = "implementation",
                        ReviewRole = "review",
                        Priority = "high",
                    },
                ],
            };
            var queuePath = Path.Combine(root, ".intent-cli", "queue-state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(queuePath)!);
            File.WriteAllText(queuePath, QueueStateSerializer.Serialize(queueState));

            Assert.True(
                NotifyDelegationExecutionEvidence.TryResolveDownstreamReference(
                    root,
                    parent,
                    "G796",
                    out var evidence,
                    out var error),
                error);
            Assert.Contains("queue-state:execution_unit=G796", evidence, StringComparison.Ordinal);
            output.WriteLine(
                $"G796 AC6/AC8 positive_carrier=queue-state; reference=G796; accepted=true; evidence={evidence}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }

    [Fact]
    public void RealPendingReferenceResolvesThroughTheG788EvidencePath_G796()
    {
        var root = NewEvidenceRoot();
        try
        {
            var parent = WriteUpstreamParent(root);
            var child = parent with
            {
                TaskId = "G796-child",
                DelegatingRole = "steward",
                RecipientRole = "architect",
                RecipientIdentity = "role=architect",
                DispatchedAt = parent.DispatchedAt.AddSeconds(1),
                Ruling = null,
            };
            Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, child).Written);

            Assert.True(
                NotifyDelegationExecutionEvidence.TryResolveDownstreamReference(
                    root,
                    parent,
                    child.TaskId,
                    out var evidence,
                    out var error),
                error);
            Assert.Contains("pending-ledger", evidence, StringComparison.Ordinal);
            output.WriteLine($"G796 AC6/AC8 real_reference={child.TaskId}; accepted=true; evidence={evidence}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }

    [Theory]
    [InlineData("question", "question")]
    [InlineData("escalation", "question")]
    [InlineData("blocked", "blocked")]
    public void ProductionStewardGateAcceptsMeasuredPendingReferenceForEachJudgementKind_G796(
        string eventKind,
        string status)
    {
        var root = NewEvidenceRoot();
        try
        {
            Assert.True(NotifyRuling.TryCreate("opaque upstream ruling", "architect", null, out var ruling, out var createError), createError);
            var parent = WriteUpstreamParent(root, ruling);
            var child = parent with
            {
                TaskId = $"G796-child-{eventKind}",
                DelegatingRole = "steward",
                RecipientRole = "architect",
                RecipientIdentity = "role=architect",
                DispatchedAt = parent.DispatchedAt.AddSeconds(1),
                Ruling = null,
            };
            Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, child).Written);

            var context = new CliContext
            {
                RepoRoot = root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
                },
            };
            using var writer = new StringWriter();
            var exit = NotifyCommand.ExecuteReport(
                context,
                [
                    "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--from", "steward", "--to", "architect", "--task-id", parent.TaskId,
                    "--status", status, "--artifact", "answer.txt", "--summary", $"{eventKind} answer",
                    "--event-kind", eventKind, "--downstream-delegation-reference", child.TaskId,
                    "--routing-root", root, "--report-root", root, "--dry-run", "--format", "json",
                ],
                writer);

            using var document = JsonDocument.Parse(writer.ToString());
            var result = document.RootElement;
            Assert.NotEqual("steward-boundary-refused", result.GetProperty("cause").GetString());
            output.WriteLine($"G796 AC6/AC8 event_kind={eventKind}; reference={child.TaskId}; gate=accepted; exit={exit}; post_gate_cause={result.GetProperty("cause").GetString() ?? "<none>"}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("outbox")]
    [InlineData("event")]
    public void DownstreamReferenceMatchesEachMeasuredG788Carrier_G796(string carrier)
    {
        var root = NewEvidenceRoot();
        try
        {
            var parent = WriteUpstreamParent(root);
            var reference = $"G796-child-{carrier}";
            switch (carrier)
            {
                case "pending":
                    Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, parent with
                    {
                        TaskId = reference,
                        DelegatingRole = "steward",
                        RecipientRole = "architect",
                        RecipientIdentity = "role=architect",
                        DispatchedAt = parent.DispatchedAt.AddSeconds(1),
                        Ruling = null,
                    }).Written);
                    break;
                case "outbox":
                    Assert.True(NotifyReportOutboxStore.WriteNew(root, new NotifyReportOutboxEntry
                    {
                        Domain = parent.Domain,
                        Team = parent.Team,
                        TaskId = reference,
                        ResultNonce = "g796-child-outbox",
                        FromRole = "steward",
                        ToRole = "architect",
                        Status = "question",
                        Artifact = "child.txt",
                        Summary = "child G796 report",
                        CreatedAt = parent.DispatchedAt.AddSeconds(1),
                        DeliveryState = "delivered",
                    }).Written);
                    break;
                default:
                    Assert.True(NotifyEventWriter.TryResolveWritePath(root, parent.Domain, parent.Team, out var eventPath, out var eventError), eventError);
                    NotifyEventWriter.Append(eventPath, new NotifyDesignEvent
                    {
                        Timestamp = parent.DispatchedAt.AddSeconds(1),
                        Team = parent.Team,
                        Kind = "question",
                        Unit = reference,
                        Summary = "child G796 report",
                        Artifact = "child.txt",
                    });
                    break;
            }

            Assert.True(
                NotifyDelegationExecutionEvidence.TryResolveDownstreamReference(
                    root,
                    parent,
                    reference,
                    out var evidence,
                    out var error),
                error);
            Assert.Contains(carrier == "pending" ? "pending-ledger" : carrier == "outbox" ? "report-outbox" : "notification-events", evidence, StringComparison.Ordinal);
            output.WriteLine($"G796 AC6/AC8 carrier={carrier}; reference={reference}; accepted=true; evidence={evidence}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }

    [Fact]
    public void StewardRelayRequiresTheRecordedUpstreamRuling_G796()
    {
        var root = NewEvidenceRoot();
        try
        {
            var missing = WriteUpstreamParent(root);
            Assert.False(NotifyRulingRelay.TryResolveUpstreamArchitectRuling(missing, out _, out var missingError));
            Assert.Contains("no upstream Architect ruling", missingError, StringComparison.OrdinalIgnoreCase);

            Assert.True(NotifyRuling.TryCreate("architect ruling", "architect", null, out var source, out var sourceError), sourceError);
            var forgedOrigin = new NotifyRuling { Payload = source!.Payload, Digest = source.Digest, Origin = "steward" };
            Assert.False(NotifyRulingRelay.TryRelay(source, forgedOrigin, null, out var forgedResult));
            Assert.Equal("ruling-origin-mismatch", forgedResult.Cause);

            var mutated = new NotifyRuling
            {
                Payload = source.Payload + "!",
                Digest = NotifyRulingRelay.ComputeDigest(source.Payload + "!"),
                Origin = source.Origin,
            };
            Assert.False(NotifyRulingRelay.TryRelay(source, mutated, null, out var mutatedResult));
            Assert.Equal("ruling-digest-mismatch", mutatedResult.Cause);
            output.WriteLine($"G796 AC3/AC4/AC5 missing_upstream=refused; forged_origin={forgedResult.Cause}; one_byte_mutation={mutatedResult.Cause}; source_digest={source.Digest}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("opencode")]
    public void StewardBoundaryIgnoresRecordedRuntimeAndModel_G796(string runtime)
    {
        Assert.False(NotifyRulingRelay.TryValidateStewardAnswer(
            "steward",
            "question",
            "architect",
            downstreamDelegationReference: null,
            out var error));
        Assert.Contains("Architect", error, StringComparison.Ordinal);
        output.WriteLine($"G796 AC9 runtime={runtime}; model-independent=true; accepted=false; error={error}");
    }

    [Fact]
    public void NoStewardPreservesTheExistingDestinationForAllKinds_G796()
    {
        foreach (var eventKind in NotifyEventKindRouting.SupportedKinds)
        {
            var currentTarget = eventKind == NotifyEventKindRouting.Escalation ? "design" : "review";
            Assert.Equal(
                currentTarget,
                NotifyEventKindRouting.ResolveTarget(
                    eventKind,
                    currentTarget,
                    stewardRecorded: false,
                    fallbackTarget: "design"));
            output.WriteLine($"G796 AC10 event_kind={eventKind}; steward_recorded=false; routed_to={currentTarget}; unchanged=true");
        }
    }

    private static string NewEvidenceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"intent-g796-routing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteEvidenceRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static NotifyPendingDelegation WriteUpstreamParent(
        string root,
        NotifyRuling? ruling = null)
    {
        var parent = new NotifyPendingDelegation
        {
            Domain = "intent-cli",
            Team = "intent-cli-dev",
            TaskId = "G796-parent",
            DelegatingRole = "architect",
            RecipientRole = "steward",
            ReportToRole = "architect",
            RecipientIdentity = "role=steward",
            ExpectedArtifact = "parent-result.txt",
            ExpectedArtifacts = ["parent-result.txt"],
            Objective = "coordinate G796 judgement",
            Inputs = ["fixture"],
            ResultNonce = "g796-parent-nonce",
            DispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            TransportMode = "herdr-only",
            Cwd = root,
            Kind = "steward",
            LaunchArguments = ["fixture"],
            Ruling = ruling,
        };
        var write = NotifyPendingDelegationStore.WriteDispatch(root, parent);
        if (!write.Written)
        {
            throw new InvalidOperationException(write.Error);
        }

        return parent;
    }

    private void AssertPendingReferenceRejected(
        string requestedReference,
        string recordedReference,
        string negativeClass)
    {
        var root = NewEvidenceRoot();
        try
        {
            var parent = WriteUpstreamParent(root);
            var child = parent with
            {
                TaskId = recordedReference,
                DelegatingRole = "steward",
                RecipientRole = "architect",
                RecipientIdentity = "role=architect",
                DispatchedAt = parent.DispatchedAt.AddSeconds(1),
                Ruling = null,
            };
            Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, child).Written);

            Assert.False(
                NotifyDelegationExecutionEvidence.TryResolveDownstreamReference(
                    root,
                    parent,
                    requestedReference,
                    out _,
                    out var error));
            Assert.Contains("did not resolve", error, StringComparison.OrdinalIgnoreCase);
            output.WriteLine(
                $"G796 AC6/AC8 negative_class={negativeClass}; requested_reference={requestedReference}; recorded_reference={recordedReference}; accepted=false; error={error}");
        }
        finally
        {
            DeleteEvidenceRoot(root);
        }
    }
}
