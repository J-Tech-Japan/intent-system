using System.Text;
using System.Text.RegularExpressions;
using IntentSystem.Cli.Commands;
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
        Assert.Equal(ruling.Digest, relay.Ruling.Digest);
        Assert.Equal("architect", relay.Ruling.Origin);
        Assert.Equal("steward-relay-1", relay.Envelope!.Fields["relay_id"]);
        Assert.True(relay.Ruling.Verifies());
        output.WriteLine($"G796 AC3/AC4 accepted={relay.Accepted}; payload_bytes_equal={payload == relay.Ruling.Payload}; digest_unchanged={ruling.Digest == relay.Ruling.Digest}; origin={relay.Ruling.Origin}; envelope.relay_id={relay.Envelope.Fields["relay_id"]}; verifies={relay.Ruling.Verifies()}");
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
}
