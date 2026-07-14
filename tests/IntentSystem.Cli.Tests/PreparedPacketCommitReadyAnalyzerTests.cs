using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class PreparedPacketCommitReadyAnalyzerTests
{
    private const string CanonicalPacketYaml = """
implementation_issue_packet:
  source_execution_unit: Z4R-G3
  issue_title: Demo packet
  target_repo: J-Tech-Creations/Zero4Racer
""";

    private const string CanonicalGithubBody = """
# Z4R-G3 demo packet

## Goal
Demo.

## Why This Slice Exists Now
Demo.

## Current Observed State
Demo.

## Accepted Baseline You May Assume
Demo.

## Target Repo / Path / Part
Demo.

## In Scope
Demo.

## Out Of Scope
Demo.

## Acceptance Criteria
Demo.

## Verification
Demo.

## Related Links
Demo.
""";

    private const string CanonicalReviewContext = """
# Z4R-G3 review context

Demo.
""";

    private const string CanonicalImplementation = """
# Z4R-G3 implementation

Demo.
""";

    [Fact]
    public void Analyze_AllFourFilesAndMatchingBindings_ReturnsCommitReady()
    {
        // G361 AC1: complete prepared packet directory with the four
        // canonical files and a matching domain binding regex returns
        // a safe commit-ready result with deterministic verified files.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady, result.Classification);
        Assert.Equal("Z4R-G3", result.ExecutionUnit);
        Assert.Equal(".intent-cli/issues/Z4R-G3/", result.PacketDirectory);
        Assert.NotNull(result.VerifiedFiles);
        Assert.Equal(4, result.VerifiedFiles!.Count);
        Assert.Contains(".intent-cli/issues/Z4R-G3/packet.yaml", result.VerifiedFiles);
        Assert.Contains(".intent-cli/issues/Z4R-G3/implementation.md", result.VerifiedFiles);
        Assert.Contains(".intent-cli/issues/Z4R-G3/review-context.md", result.VerifiedFiles);
        Assert.Contains(".intent-cli/issues/Z4R-G3/github-body.md", result.VerifiedFiles);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Analyze_MissingGithubBody_ReturnsUnsafeMissingCanonicalFile()
    {
        // G361 AC2: missing github-body.md returns unsafe with structured
        // missing-canonical-file reason and lists the missing path.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = null,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonMissingCanonicalFile, result.Reason);
        Assert.NotNull(result.MissingFiles);
        Assert.Single(result.MissingFiles!);
        Assert.Contains(".intent-cli/issues/Z4R-G3/github-body.md", result.MissingFiles!);
    }

    [Fact]
    public void Analyze_WrongDomain_ReturnsUnsafeWrongDomain()
    {
        // G361 AC3: SKS-G<N> packet while the active domain regex targets
        // ^Z4R-G[0-9]+$ returns unsafe with wrong-domain reason; the
        // packet content is otherwise complete (test isolates the
        // cross-domain check).
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "SKS-G42",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonWrongDomain, result.Reason);
        Assert.Equal("^Z4R-G[0-9]+$", result.DomainRegex);
        Assert.Contains("SKS-G42", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_WrongTargetRepo_ReturnsUnsafeWrongTargetRepo()
    {
        // G361 AC4: packet declares a different target_repo than the host
        // loop expects; return unsafe with wrong-target-repo and surface
        // both declared and requested repos so the operator can resolve
        // the mismatch.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "Other/Repo",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonWrongTargetRepo, result.Reason);
        Assert.Equal("Other/Repo", result.RequestedTargetRepo);
        Assert.Equal("J-Tech-Creations/Zero4Racer", result.DeclaredTargetRepo);
    }

    [Fact]
    public void Analyze_MissingGithubBodySection_ReturnsUnsafeMissingSection()
    {
        // G361: github-body.md without one of the required standalone
        // sections returns unsafe with the structured section name; this
        // mirrors the issue contract validation in MetadataValidate so
        // the host loop never auto-commits an incomplete child issue.
        const string incompleteBody = """
# Title
## Goal
## Why This Slice Exists Now
## Current Observed State
""";
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = incompleteBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonGithubBodyMissingSection, result.Reason);
        Assert.NotNull(result.MissingGithubBodySection);
    }

    [Fact]
    public void Analyze_NoBindingRegex_SkipsCrossDomainCheck()
    {
        // Fail-open: when the host has not configured a binding regex
        // the analyzer must not block on cross-domain (mirrors G359
        // posture). All other checks still apply.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "SKS-G99",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = null,
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady, result.Classification);
    }

    [Fact]
    public void Analyze_InvalidBindingRegex_FailsOpenAndAccepts()
    {
        // Defensive: a malformed binding regex must not indefinitely
        // block the host loop. The analyzer falls back to "no check"
        // when the regex won't compile (mirrors G359 fail-open).
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "[unclosed",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady, result.Classification);
    }

    [Fact]
    public void Analyze_NoTargetRepoRequested_ReturnsUnsafeMissingTargetRepo()
    {
        // PR #824 review repair #7: the prepared-packet lane MUST NOT
        // classify a packet as commit-ready without verifying its
        // declared target_repo. When RequestedTargetRepo is null,
        // fail closed with `missing-target-repo` so an invocation
        // without `--target-repo` cannot bypass the child-repo
        // binding check.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = null,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonMissingTargetRepo, result.Reason);
    }

    [Fact]
    public void Analyze_RequireDomainBinding_MissingRegex_ReturnsUnsafeMissingBinding()
    {
        // PR #824 review repair #2: when the host requires a domain
        // binding (`--domain` was supplied), missing
        // `execution_unit_regex` is an unsafe stop — the packet must
        // not be auto-committed without verifying the domain
        // boundary. Reason is structured for operator triage.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = null,
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonMissingDomainBindingRegex, result.Reason);
    }

    [Fact]
    public void Analyze_RequireDomainBinding_InvalidRegex_ReturnsUnsafeInvalidBinding()
    {
        // PR #824 review repair #2: when the host requires a domain
        // binding and the bindings.md `execution_unit_regex` does not
        // compile, fail closed with `invalid-domain-binding-regex`
        // instead of silently bypassing the domain check.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "[unclosed",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonInvalidDomainBindingRegex, result.Reason);
        Assert.Equal("[unclosed", result.DomainRegex);
    }

    [Fact]
    public void Analyze_MalformedPacketYaml_TabIndentation_ReturnsUnsafeUnparseable()
    {
        // PR #824 review repair #2: tab character in indentation is
        // invalid YAML (1.2 §6.1). The strict parser fails closed so
        // `packet-yaml-unparseable` is actually reachable for this
        // class of bad packet — previously the line-scanner silently
        // ignored it.
        const string tabIndentedPacket = "implementation_issue_packet:\n\tsource_execution_unit: Z4R-G3\n\ttarget_repo: J-Tech-Creations/Zero4Racer\n";
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = tabIndentedPacket,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, result.Reason);
        Assert.Contains("tab", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_MalformedPacketYaml_MissingColon_ReturnsUnsafeUnparseable()
    {
        // A non-blank, non-comment line that doesn't have a `key: value`
        // shape is malformed YAML. Previously the line-scanner silently
        // ignored it; now the parser fails closed.
        const string missingColonPacket = "target_repo J-Tech-Creations/Zero4Racer\nsource_execution_unit: Z4R-G3\n";
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = missingColonPacket,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, result.Reason);
    }

    [Fact]
    public void Analyze_MissingTargetRepo_ReturnsUnsafe_EvenWhenOtherChecksPass()
    {
        // PR #824 review repair #7: a complete packet with bindings
        // but no `RequestedTargetRepo` MUST return
        // `missing-target-repo` rather than commit-ready. Closes
        // the bypass where an invocation without `--target-repo`
        // could silently classify a packet as commit-ready.
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = null,
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonMissingTargetRepo, result.Reason);
    }

    [Fact]
    public void Analyze_GithubBody_NonStandaloneHeading_ReturnsUnsafeMissingSection()
    {
        // PR #824 review repair #5: `## My Goal` is a partial /
        // non-standalone heading that the old substring-Contains
        // match accepted. The strict exact-match rejects it so the
        // packet is classified missing-section.
        const string nonStandaloneGoal = """
            # Title

            ## My Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification
            x

            ## Related Links
            x
            """;
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = nonStandaloneGoal,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonGithubBodyMissingSection, result.Reason);
        Assert.Equal("Goal", result.MissingGithubBodySection);
    }

    [Fact]
    public void Analyze_GithubBody_AnnotatedHeading_ReturnsUnsafeMissingSection()
    {
        // `## Goal - notes` is also non-standalone; the strict match
        // rejects it.
        const string annotatedGoal = """
            # Title

            ## Goal - notes
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification
            x

            ## Related Links
            x
            """;
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = annotatedGoal,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonGithubBodyMissingSection, result.Reason);
        Assert.Equal("Goal", result.MissingGithubBodySection);
    }

    [Fact]
    public void Analyze_GithubBody_DraftParentheticalHeading_ReturnsUnsafeMissingSection()
    {
        // PR #824 review repair #8: confirm exact-match heading
        // rejects `## Current Observed State (draft)` — a documented
        // reviewer-cited example of a non-standalone heading that
        // must NOT satisfy the canonical `Current Observed State`
        // section. Locks the behavior for the specific phrasing the
        // reviewer surfaced.
        const string parentheticalHeading = """
            # Title

            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State (draft)
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification
            x

            ## Related Links
            x
            """;
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = CanonicalPacketYaml,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = parentheticalHeading,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonGithubBodyMissingSection, result.Reason);
        Assert.Equal("Current Observed State", result.MissingGithubBodySection);
    }

    [Fact]
    public void Analyze_PacketYamlWithApostropheInDoubleQuotedValue_G527Regression_IsCommitReady()
    {
        // G527 regression: the 2026-07-10 field incident — a correctly
        // double-quoted `placement_rationale` value containing an
        // apostrophe was rejected as `packet-yaml-unparseable`. It must now
        // be accepted end-to-end through the same analyzer
        // `queue-seed-from-packet` uses.
        const string packetYamlWithApostrophe = """
implementation_issue_packet:
  source_execution_unit: Z4R-G3
  issue_title: Demo packet
  target_repo: J-Tech-Creations/Zero4Racer
placement_rationale: "This is Sekiban's core boundary and it's the right place."
""";
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = packetYamlWithApostrophe,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady, result.Classification);
    }

    [Fact]
    public void Analyze_MalformedPacketYaml_UnbalancedQuote_ReturnsUnsafeUnparseable()
    {
        // An operator typo that breaks scalar parsing (unbalanced
        // quote) must fail closed so the packet isn't auto-committed
        // with corrupt content.
        const string unbalancedQuotePacket = "implementation_issue_packet:\n  source_execution_unit: Z4R-G3\n  target_repo: \"J-Tech-Creations/Zero4Racer\n";
        var result = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = "Z4R-G3",
            PacketYaml = unbalancedQuotePacket,
            ImplementationMarkdown = CanonicalImplementation,
            ReviewContextMarkdown = CanonicalReviewContext,
            GithubBodyMarkdown = CanonicalGithubBody,
            ExecutionUnitRegex = "^Z4R-G[0-9]+$",
            RequestedTargetRepo = "J-Tech-Creations/Zero4Racer",
            RequireDomainBinding = true,
        });

        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Equal(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, result.Reason);
    }
}
