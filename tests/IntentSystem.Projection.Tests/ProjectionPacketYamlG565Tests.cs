using IntentSystem.Projection.Serialization;

namespace IntentSystem.Projection.Tests;

/// <summary>
/// G565: projection accepts exactly what the packet schema accepts.
///
/// The field report (sekiban-as-a-service design thread, 2026-07-31, v0.6.2):
/// <c>intent-cli clarify open SKS-G837</c> rejected an existing, otherwise
/// valid packet with "Projection packet YAML contains invalid section header"
/// because its title carried an em-dash and long punctuation. The reporter's
/// diagnosis was exact — the two parsers disagreed, and the packet surfaces
/// were the ones parsing real YAML.
///
/// The defect was never one title. It was that
/// <c>ProjectionPacketSerializer</c> hand-rolled a line reader, so EVERY legal
/// YAML construct it failed to anticipate became a projection-only failure:
/// G534 patched block-sequence indentation, G561 patched required-section
/// rejection, and this would have continued one construct at a time. These
/// fixtures pin the constructs that used to break it, and each one is a
/// standing statement that projection speaks YAML rather than an approximation
/// of it.
/// </summary>
public sealed class ProjectionPacketYamlG565Tests
{
    /// <summary>
    /// The reported case: an em-dash, long punctuation, and — the part the hand
    /// parser could never have survived — a <c>": "</c> INSIDE a quoted scalar.
    /// The old reader split fields on the first colon it found, so the value
    /// silently truncated where the packet schema reads one whole string.
    /// </summary>
    [Fact]
    public void Deserialize_GivenEmDashAndQuotedColonSpaceTitle_RoundTripsTheWholeString_G565()
    {
        const string Title = "SKS-G837: Query surface — read models, projections: the whole set (round 2)";

        var packet = ProjectionPacketSerializer.Deserialize(
            BuildPacket(issueTitle: $"\"{Title}\"", executionUnit: "SKS-G837"));

        Assert.Equal(Title, packet.ImplementationIssuePacket.IssueTitle);
        Assert.Equal("SKS-G837", packet.ImplementationIssuePacket.SourceExecutionUnit);
    }

    [Theory]
    // Every one of these is legal YAML the packet surfaces already accept, and
    // every one of them is a shape the hand-rolled reader either mishandled or
    // rejected outright.
    [InlineData("'single-quoted — with an em-dash'", "single-quoted — with an em-dash")]
    [InlineData("\"escaped \\\"quotes\\\" inside\"", "escaped \"quotes\" inside")]
    [InlineData("\"a tab\\tseparated value\"", "a tab\tseparated value")]
    [InlineData("plain scalar — unquoted, with punctuation!", "plain scalar — unquoted, with punctuation!")]
    [InlineData("\"trailing spaces preserved   \"", "trailing spaces preserved   ")]
    [InlineData("\"日本語タイトル: コロン付き\"", "日本語タイトル: コロン付き")]
    public void Deserialize_GivenLegalScalarForms_ReadsThemAsTheSchemaDoes_G565(string yamlValue, string expected)
    {
        var packet = ProjectionPacketSerializer.Deserialize(BuildPacket(issueTitle: yamlValue));

        Assert.Equal(expected, packet.ImplementationIssuePacket.IssueTitle);
    }

    [Fact]
    public void Deserialize_GivenCommentsAnywhere_IgnoresThem_G565()
    {
        // The hand parser treated every column-0 line as a section header, so a
        // top-level comment was "invalid section header" — the exact message
        // the field report carried.
        var yaml = "# top-level comment, at column 0\n"
            + BuildPacket(extraImplementationLines: "  # an indented comment\n")
            + "\n# a trailing comment\n";

        var packet = ProjectionPacketSerializer.Deserialize(yaml);

        Assert.Equal("G565", packet.ImplementationIssuePacket.SourceExecutionUnit);
    }

    [Fact]
    public void Deserialize_GivenFlowSequences_ReadsThemAsLists_G565()
    {
        // `dependencies: [G1, G2]` is ordinary YAML. The old reader stored the
        // literal text "[G1, G2]" as a SCALAR, so a packet using flow style
        // failed with "field must be a list" — while `queue-seed-from-packet`
        // happily expanded the same line.
        var packet = ProjectionPacketSerializer.Deserialize(
            BuildPacket(dependencies: "[G1, G2]"));

        Assert.Equal(["G1", "G2"], packet.ImplementationIssuePacket.Dependencies);
    }

    [Fact]
    public void Deserialize_GivenFoldedAndLiteralBlockScalars_ReadsTheWholeValue_G565()
    {
        var yaml = BuildPacket(goal: ">-\n    a folded goal that wraps\n    across two source lines");

        var packet = ProjectionPacketSerializer.Deserialize(yaml);

        Assert.Equal("a folded goal that wraps across two source lines", packet.ImplementationIssuePacket.Goal);
    }

    [Fact]
    public void Deserialize_GivenBothBlockSequenceIndentations_StillParsesIdentically_G565()
    {
        // G534's fix was a patch to the hand parser. Parsing YAML with a YAML
        // parser subsumes it — pinned here so the behaviour cannot regress
        // when the patch it replaced is gone.
        var twoSpace = ProjectionPacketSerializer.Deserialize(
            BuildPacket(dependencies: "\n  - G1\n  - G2"));
        var fourSpace = ProjectionPacketSerializer.Deserialize(
            BuildPacket(dependencies: "\n    - G1\n    - G2"));

        Assert.Equal(["G1", "G2"], twoSpace.ImplementationIssuePacket.Dependencies);
        Assert.Equal(twoSpace.ImplementationIssuePacket.Dependencies, fourSpace.ImplementationIssuePacket.Dependencies);
    }

    [Fact]
    public void Deserialize_GivenOptionalNonSectionTopLevelKeys_IgnoresThem_G565()
    {
        // A top-level scalar or sequence is not a section. It is not an error
        // either: only the sections this contract REQUIRES are enforced.
        var yaml = "schema_version: 1\n"
            + "aliases:\n  - one\n  - two\n"
            + BuildPacket();

        var packet = ProjectionPacketSerializer.Deserialize(yaml);

        Assert.Equal("G565", packet.ImplementationIssuePacket.SourceExecutionUnit);
    }

    // ------------------------------------------------ contract still enforced

    [Fact]
    public void Deserialize_StillRejectsAMissingRequiredSection_G565()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize("implementation_issue_packet:\n  issue_title: \"x\"\n"));

        Assert.Contains("must contain required section 'review_context_packet'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_StillRejectsAMissingRequiredField_G565()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize(BuildPacket(omitIssueKind: true)));

        Assert.Contains("must contain required field 'issue_kind'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_StillRejectsAValuelessRequiredScalar_G565()
    {
        // Byte-compatible with the previous reader: a `key:` with no value was
        // an empty LIST, so a required scalar left blank failed as "must be a
        // scalar string" rather than passing as an empty string.
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize(BuildPacket(issueTitle: string.Empty)));

        Assert.Contains("'issue_title' must be a scalar string", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_StillRejectsAScalarWhereAListIsRequired_G565()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize(BuildPacket(dependencies: "\"not a list\"")));

        Assert.Contains("'dependencies' must be a list", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_StillRejectsMismatchedExecutionUnits_G565()
    {
        var yaml = BuildPacket(executionUnit: "G565").Replace(
            "  source_execution_unit: \"G565\"\n  parent_intent_root",
            "  source_execution_unit: \"G999\"\n  parent_intent_root",
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize(yaml));

        Assert.Contains("must match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsYamlThatIsNotYaml_WithAParseDiagnostic_G565()
    {
        // Genuinely broken YAML still fails — and now says so as a parse
        // failure rather than as a guess about section headers.
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize("implementation_issue_packet:\n  a: [1, 2\n"));

        Assert.Contains("could not be parsed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsATopLevelDocumentThatIsNotAMapping_G565()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize("- just\n- a sequence\n"));

        Assert.Contains("is not a mapping", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A complete, valid packet. Every parameter defaults to the canonical
    /// form, so each fixture above varies exactly one construct.
    /// </summary>
    private static string BuildPacket(
        string issueTitle = "\"G565 packet\"",
        string executionUnit = "G565",
        string goal = "\"unify the packet YAML parsing pathway\"",
        string dependencies = "[]",
        string extraImplementationLines = "",
        bool omitIssueKind = false)
    {
        var issueKindLine = omitIssueKind ? string.Empty : "  issue_kind: \"feature\"\n";

        return $"""
            implementation_issue_packet:
              issue_title: {issueTitle}
            {issueKindLine}  source_execution_unit: "{executionUnit}"
              goal: {goal}
              in_scope:
                - "one"
              out_of_scope:
                - "two"
              target_repo: "J-Tech-Japan/intent-system"
              target_path: "src/IntentSystem.Projection/**"
              target_part: "packet parsing pathway"
              dependencies: {dependencies}
              technical_baseline:
                - "C# / .NET"
              project_local_guide:
                - "AGENTS.md"
              intent_baseline:
                - "source of truth remains in the parent intent repo"
              intent_references:
                - "intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md"
              rules_and_specs:
                - "intents/rules/issue-projection-format.md"
              acceptance_criteria:
                - "one parsing pathway"
              verification_evidence:
                - "tests-passing"
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"
            {extraImplementationLines}
            review_context_packet:
              source_execution_unit: "{executionUnit}"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "intents/intent-cli/intent-tree/means/03-state-and-audit-strategy.md"
              rules_and_specs:
                - "intents/rules/issue-projection-format.md"
              acceptance_criteria:
                - "one parsing pathway"
              deterministic_review_checks:
                - "the hand parser is gone"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """;
    }
}
