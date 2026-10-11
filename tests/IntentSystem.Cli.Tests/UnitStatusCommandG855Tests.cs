using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class UnitStatusCommandG855Tests
{
    private const string HeadSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SourceIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1878";
    private const string ForeignIssueUrl = "https://github.com/tomohisa/toy-calc-sample/issues/3";
    private static readonly DateTimeOffset G864RulingTime = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
    private static string LegacyForeignSourceReport => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G859", "legacy-observed-report.yaml"));

    private static string SnapshotFiles(string root, string? excludedRelativePath = null)
    {
        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(value => value, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, path)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (excludedRelativePath is not null && string.Equals(relative, excludedRelativePath, StringComparison.Ordinal))
                    continue;

                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    FileSystemInfo link = (attributes & FileAttributes.Directory) != 0
                        ? new DirectoryInfo(path)
                        : new FileInfo(path);
                    entries.Add("L:" + relative + "->" + link.LinkTarget);
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    entries.Add("D:" + relative + "/");
                    pending.Push(path);
                    continue;
                }

                entries.Add("F:" + relative + "=" + Convert.ToHexString(File.ReadAllBytes(path)));
            }
        }
        return string.Join("\n", entries.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static JsonDocument RunStatusJson(TempHost host, FixedReader reader, out int exit)
    {
        return JsonDocument.Parse(RunStatusOutput(host, reader, "json", out exit));
    }

    private static string RunStatusOutput(TempHost host, FixedReader reader, string format, out int exit)
    {
        using var writer = new StringWriter();
        exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], writer, reader);
        return writer.ToString();
    }

    private static string RunStatusOutputForRoot(string root, FixedReader reader, string format, out int exit)
    {
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", MetadataSourceBranch = "metadata" },
            },
        };
        exit = UnitStatusCommand.ExecuteCore(context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], writer, reader);
        return writer.ToString();
    }

    private static string RunStatusPublicOutputForRoot(string root, string format, out int exit)
    {
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
            },
        };
        exit = UnitStatusCommand.Execute(context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], writer);
        return writer.ToString();
    }

    private static void ConfigureStatusGit(string root)
    {
        RunStatusGit(root, "config", "user.name", "G864 status test operator");
        RunStatusGit(root, "config", "user.email", "g864-status-test@example.invalid");
    }

    private static string RunStatusGit(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git process did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("git test command exceeded its bounded timeout");
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr.Result}");
        return stdout.Result;
    }

    private static string SnapshotGitRefs(string root) =>
        RunStatusGit(root, "for-each-ref", "--format=%(refname) %(objectname)");

    private static Dictionary<string, string> SnapshotRenderedFacts(JsonElement report, string? excludedFactId = null) =>
        report.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()
                .Where(fact => fact.GetProperty("id").GetString() != excludedFactId)
                .Select(fact => (Key: step.GetProperty("id").GetString() + "/" + fact.GetProperty("id").GetString(), Value: fact.GetRawText())))
            .ToDictionary(item => item.Key!, item => item.Value, StringComparer.Ordinal);

    private static void AssertFactUnavailableClass(JsonElement fact, string? expected)
    {
        if (expected is null)
        {
            if (fact.TryGetProperty("unavailable_class", out var value)) Assert.Equal(JsonValueKind.Null, value.ValueKind);
            return;
        }
        Assert.Equal(expected, fact.GetProperty("unavailable_class").GetString());
    }

    private static void AssertMarkdownFactUnavailableClass(string markdown, string factId, string? expected)
    {
        var marker = "### " + factId + ":";
        var start = markdown.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Markdown fact section was not present: " + factId);
        var next = markdown.IndexOf("\n### ", start + marker.Length, StringComparison.Ordinal);
        var section = next < 0 ? markdown[start..] : markdown[start..next];
        var expectedLine = "- unavailable_class: " + (expected is null ? "null" : "`" + expected + "`");
        Assert.Contains(expectedLine, section, StringComparison.Ordinal);
    }

    private static JsonElement FindBugSource(JsonElement report) => TempHost.FindSubcheck(report, "bug-chain-or-ruling");

    private static string WriteActualG864Ruling(TempHost host, string id = "R-G864-STATUS",
        string domain = "intent-cli", string team = "intent-cli-dev", string targetRepo = "J-Tech-Japan/intent-system",
        string scopeKind = "execution-units", IReadOnlyList<string>? executionUnits = null,
        DateTimeOffset? recordedAt = null, DateTimeOffset? expiresAt = null, IReadOnlyList<string>? supersedes = null)
    {
        var recorded = recordedAt ?? G864RulingTime;
        var record = new RulingArtifact
        {
            Id = id,
            Domain = domain,
            Team = team,
            AuthorityRole = "operator",
            TargetRepo = targetRepo,
            ScopeKind = scopeKind,
            ExecutionUnits = executionUnits ?? (scopeKind == "repository" ? [] : ["G855"]),
            Decision = "Keep the current bounded implementation choice.",
            Rationale = "The current packet is within the declared scope.",
            EvidenceRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/1889"],
            RecordedAt = recorded,
            ExpiresAt = expiresAt,
            Supersedes = supersedes ?? [],
        };
        var inputPath = Path.Combine(host.Root, "g864-ruling-input.json");
        var canonicalBytes = RulingArtifact.Serialize(record);
        File.WriteAllBytes(inputPath, canonicalBytes);
        using var writer = new StringWriter();
        var exit = RulingCommand.ExecuteRecord(host.Context,
            ["--id", id, "--domain", record.Domain, "--team", record.Team, "--from-file", inputPath,
                "--authority-role", "operator", "--write", "--format", "json"],
            writer, beforeOperation: null, nowOverride: recorded);
        Assert.True(exit == 0, writer.ToString());
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.Equal("written", result.RootElement.GetProperty("disposition").GetString());
        Assert.True(result.RootElement.GetProperty("wrote").GetBoolean());
        var artifactPath = Path.Combine(host.Root, ".intent-cli", "rulings", record.Domain, record.Team, id + ".json");
        Assert.True(File.Exists(artifactPath));
        var actualBytes = File.ReadAllBytes(artifactPath);
        Assert.Equal(canonicalBytes, actualBytes);
        return Convert.ToHexString(SHA256.HashData(actualBytes)).ToLowerInvariant();
    }

    private static string G864ExpectedPublicBlock(string sha256, string id = "R-G864-STATUS",
        string domain = "intent-cli", string team = "intent-cli-dev", string targetRepo = "J-Tech-Japan/intent-system", string unit = "G855") => string.Join('\n',
    [
        "<!-- intent-cli:scope-sources:v1 -->",
        "### Ruling scope provenance",
        $"- ruling:{id} | domain={domain} | team={team} | repo={targetRepo} | unit={unit} | sha256={sha256}",
        "Authority: supplied-not-authenticated. Publication: not-verified.",
        "<!-- /intent-cli:scope-sources:v1 -->",
    ]);

    private static void WriteG864SourcePacket(TempHost host, string sha256, string id = "R-G864-STATUS",
        string sourceDomain = "intent-cli", string sourceTeam = "intent-cli-dev",
        string sourceRepo = "J-Tech-Japan/intent-system", string unit = "G855", string? body = null,
        string? sourceArtifact = null)
    {
        var directory = Path.Combine(host.Root, ".intent-cli", "issues", "G855");
        Directory.CreateDirectory(directory);
        var legacySourceLine = sourceArtifact is null ? string.Empty : "  source_artifact: \"" + sourceArtifact + "\"\n";
        var packet = $$"""
            schema_version: 1
            implementation_issue_packet:
              source_execution_unit: {{unit}}
              domain: {{sourceDomain}}
              team: {{sourceTeam}}
              target_repo: {{sourceRepo}}
            {{legacySourceLine}}scope_sources:
              - "ruling:{{id}}"
            scope_source_digests:
              "ruling:{{id}}": "{{sha256}}"
            """;
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), packet + "\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# G855 implementation\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# G855 review context\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "github-body.md"),
            body ?? ("# G855 request\n\n" + G864ExpectedPublicBlock(sha256, id, sourceDomain, sourceTeam, sourceRepo, unit) + "\n"), new UTF8Encoding(false));
    }

    private static string SetTopLevelYamlField(string yaml, string fieldName, string replacement)
    {
        var lines = yaml.Split('\n').ToList();
        var start = lines.FindIndex(line => line.StartsWith(fieldName + ":", StringComparison.Ordinal));
        var replacementLines = replacement.TrimEnd('\n').Split('\n');
        if (start < 0)
        {
            lines.AddRange(replacementLines);
            return string.Join('\n', lines);
        }

        var end = start + 1;
        while (end < lines.Count && lines[end].StartsWith("  - ", StringComparison.Ordinal)) end++;
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, replacementLines);
        return string.Join('\n', lines);
    }

    private static string RemoveTopLevelYamlField(string yaml, string fieldName) =>
        string.Join('\n', yaml.Split('\n').Where(line => !line.StartsWith(fieldName + ":", StringComparison.Ordinal)));

    private static void AssertMalformedSourceInventoryReport(TempHost host, string reportFileName, string yaml, string sourceUrl)
    {
        Assert.Throws<InvalidOperationException>(() => BugReportArtifactYaml.Deserialize(yaml));
        var reportPath = Path.Combine(host.Root, ".intent-cli", "bugs", reportFileName);
        File.WriteAllText(reportPath, yaml);
        var bytes = File.ReadAllBytes(reportPath);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = FindBugSource(report.RootElement);
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        Assert.Contains(reportFileName, source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains(sourceUrl, source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
        Assert.Equal("bug-report-artifact", attempted.GetProperty("kind").GetString());
        Assert.Equal(".intent-cli/bugs/" + reportFileName, attempted.GetProperty("path").GetString());
        Assert.Equal(sourceUrl, attempted.GetProperty("url").GetString());
        Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
        Assert.Equal(bytes, File.ReadAllBytes(reportPath));
    }

    private static FixedReader CreateKnownSixProvenanceLimitsReader()
    {
        const string repo = "J-Tech-Japan/intent-system";
        const int pullRequest = 1864;
        const string pullRequestUrl = "https://github.com/J-Tech-Japan/intent-system/pull/1864";
        var approvalEvidence = new UnitStatusEvidencePointer
        {
            Kind = "pull-request-label",
            Url = pullRequestUrl,
            ExecutionUnit = "G855",
            Repo = repo,
            Pr = pullRequest,
            HeadSha = HeadSha,
            Provenance = "github-rest",
        };
        return new FixedReader(
            new UnitStatusRemoteSnapshot
            {
                State = "completed",
                HeadBefore = HeadSha,
                HeadAfter = HeadSha,
                HeadSha = HeadSha,
                Facts =
                [
                    new UnitStatusFact
                    {
                        Id = "approval-head-receipt",
                        State = UnitStatusStates.Unavailable,
                        Cause = "approval-head-receipt-not-recorded",
                        UnavailableClass = UnitStatusStates.ProvenanceLimit,
                        RepairUnavailableReason = "no-supported-historical-receipt-writer",
                        Evidence = [approvalEvidence],
                    },
                ],
            },
            new UnitStatusClaimSnapshot
            {
                State = UnitStatusStates.Unavailable,
                Cause = "local-claim-ref-unavailable",
                Detail = "local-claim-ref-unavailable: no configured local metadata branch is available; this reader does not infer or fetch a canonical claim branch.",
                UnavailableClass = UnitStatusStates.ProvenanceLimit,
            });
    }

    private static void AssertKnownSixProvenanceLimits(JsonElement report, int expectedObservationExitCode = 0)
    {
        var expectedIds = new[]
        {
            "approval-head-receipt",
            "design-claim-acquired",
            "design-claim-release",
            "implementation-claim-acquired",
            "implementation-claim-release",
            "worker-completion-receipt",
        };
        var allFacts = report.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()).ToArray();
        var actualIds = allFacts
            .Where(fact => fact.TryGetProperty("unavailable_class", out var unavailableClass)
                && unavailableClass.GetString() == UnitStatusStates.ProvenanceLimit)
            .Select(fact => fact.GetProperty("id").GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedIds.OrderBy(id => id, StringComparer.Ordinal), actualIds);
        Assert.Equal(6, report.GetProperty("summary").GetProperty("unavailable_class_counts")
            .GetProperty(UnitStatusStates.ProvenanceLimit).GetInt32());
        Assert.Equal(expectedObservationExitCode, report.GetProperty("summary").GetProperty("observation_exit_code").GetInt32());

        foreach (var id in expectedIds.Where(id => id.EndsWith("claim-acquired", StringComparison.Ordinal)
            || id.EndsWith("claim-release", StringComparison.Ordinal)))
        {
            var fact = TempHost.FindSubcheck(report, id);
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("local-claim-ref-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("provenance-limit", fact.GetProperty("unavailable_class").GetString());
            Assert.Empty(fact.GetProperty("repair_commands").EnumerateArray());
            Assert.Equal("no-supported-repair-command-in-this-slice", fact.GetProperty("repair_unavailable_reason").GetString());
            var evidence = Assert.Single(fact.GetProperty("evidence").EnumerateArray());
            Assert.Equal("local-claim-snapshot", evidence.GetProperty("kind").GetString());
            Assert.Null(evidence.GetProperty("path").GetString());
            Assert.Null(evidence.GetProperty("record_id").GetString());
            Assert.Equal("G855", evidence.GetProperty("execution_unit").GetString());
            Assert.Equal("intent-cli-dev", evidence.GetProperty("claim_team").GetString());
            Assert.Equal("local-claim-snapshot-ref-unconfigured:scope=execution-unit:G855; ref=(unresolved); oid=(unresolved)",
                evidence.GetProperty("provenance").GetString());
        }

        var worker = TempHost.FindSubcheck(report, "worker-completion-receipt");
        Assert.Equal("unavailable", worker.GetProperty("state").GetString());
        Assert.Equal("worker-completion-receipt-not-recorded", worker.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", worker.GetProperty("unavailable_class").GetString());
        Assert.Empty(worker.GetProperty("evidence").EnumerateArray());
        Assert.Equal("no-supported-historical-worker-completion-receipt-writer", worker.GetProperty("repair_unavailable_reason").GetString());

        var approval = TempHost.FindSubcheck(report, "approval-head-receipt");
        Assert.Equal("unavailable", approval.GetProperty("state").GetString());
        Assert.Equal("approval-head-receipt-not-recorded", approval.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", approval.GetProperty("unavailable_class").GetString());
        Assert.Equal("no-supported-historical-receipt-writer", approval.GetProperty("repair_unavailable_reason").GetString());
        var approvalPointer = Assert.Single(approval.GetProperty("evidence").EnumerateArray());
        Assert.Equal("pull-request-label", approvalPointer.GetProperty("kind").GetString());
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/1864", approvalPointer.GetProperty("url").GetString());
        Assert.Equal("G855", approvalPointer.GetProperty("execution_unit").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", approvalPointer.GetProperty("repo").GetString());
        Assert.Equal(1864, approvalPointer.GetProperty("pr").GetInt32());
        Assert.Equal(HeadSha, approvalPointer.GetProperty("head_sha").GetString());
        Assert.Equal("github-rest", approvalPointer.GetProperty("provenance").GetString());
    }

    [Fact]
    public void RoleRecordEnumerationMoveNextFailureIsStructuredAndKeepsRowsReadBeforeFailure()
    {
        static IEnumerable<string> EnumeratePaths()
        {
            yield return "first-record.json";
            throw new IOException("role directory enumeration failed");
        }

        var result = UnitStatusCommand.ReadRoleRecordRows(EnumeratePaths, path => path);

        Assert.Equal(["first-record.json"], result.Rows);
        Assert.Contains(result.Errors, error => error.Contains("role directory enumeration failed", StringComparison.Ordinal));
    }

    [Fact]
    public void HostlessStatusReturnsStructuredUnavailableAndHelpRemainsMetadataFree()
    {
        using var writer = new StringWriter();
        var exit = UnitStatusCommand.ExecuteMetadataFree(
            ["unit", "status", "--execution-unit", "G855", "--format", "json"],
            "/tmp/child-without-host",
            writer);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("host-context-unavailable", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal("not-observed", document.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("state").GetString());

        using var help = new StringWriter();
        Assert.Equal(0, UnitStatusCommand.ExecuteMetadataFree(["unit", "status", "--help"], "/tmp/child", help));
        Assert.Contains("Read-only lifecycle evidence", help.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidFormatProducesStructuredJsonInsteadOfSilentlyUsingMarkdown()
    {
        using var host = new TempHost();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--format", "xml"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalid-request", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal(0, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void MissingFormatIsStructuredInvalidRequest()
    {
        using var host = new TempHost();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("invalid-request", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Equal(0, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void ExplicitDotSegmentScopesAreInvalidRequestsBeforeHostReads()
    {
        foreach (var scope in new[] { ".", ".." })
        {
            using var host = new TempHost();
            var reader = new FixedReader();
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", scope, "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(1, exit);
            Assert.Equal(0, reader.ClaimReads);
            Assert.Equal(0, reader.GitHubReads);
            using var report = JsonDocument.Parse(writer.ToString());
            Assert.Equal("invalid-request", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        }
    }

    private static void AssertKnownSixProvenanceLimitsInMarkdown(string markdown)
    {
        var expectedIds = new[]
        {
            "approval-head-receipt", "design-claim-acquired", "design-claim-release",
            "implementation-claim-acquired", "implementation-claim-release", "worker-completion-receipt",
        };
        foreach (var id in expectedIds)
            Assert.Contains("subcheck `" + id + "`: **unavailable**", markdown, StringComparison.Ordinal);
        Assert.Equal(6, markdown.Split("unavailable_class: `provenance-limit`", StringSplitOptions.None).Length - 1);
        Assert.Contains("no-supported-historical-receipt-writer", markdown, StringComparison.Ordinal);
        Assert.Contains("no-supported-repair-command-in-this-slice", markdown, StringComparison.Ordinal);
        Assert.Contains("local-claim-snapshot-ref-unconfigured:scope=execution-unit:G855; ref=(unresolved); oid=(unresolved)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactRecordedNonSoloModeReturnsNotApplicableWithoutGithubCalls()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            writer,
            reader);

        Assert.Equal(0, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("delivery", root.GetProperty("team_mode").GetString());
        Assert.Equal("current-recorded-entry", root.GetProperty("mode_basis").GetString());
        Assert.All(root.GetProperty("steps").EnumerateArray(), step => Assert.Equal("not-applicable", step.GetProperty("state").GetString()));
        Assert.Equal(26, root.GetProperty("summary").GetProperty("state_counts").GetProperty("not-applicable").GetInt32());
    }

    [Fact]
    public void LocallyEstablishedUnitWithoutPublishedIssueIsMissingEvidenceAndSkipsGithub()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var publishPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "publish.yaml");
        var publish = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishPath));
        File.WriteAllText(publishPath, IssuePublishArtifactYaml.Serialize(publish with
        {
            PublishStatus = "drafted",
            CreatedIssueNumber = null,
            CreatedIssueUrl = null,
            PublishedLabelName = null,
            LifecycleState = null,
            LinkedPrNumber = null,
            LinkedPrUrl = null,
        }));
        var before = SnapshotFiles(host.Root);
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        Assert.Equal(0, reader.GitHubReads);
        Assert.Equal(before, SnapshotFiles(host.Root));
        using var report = JsonDocument.Parse(writer.ToString());
        foreach (var id in new[]
        {
            "issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker",
            "approval-head-receipt", "pr-merged", "recorded-review",
        })
        {
            var fact = TempHost.FindSubcheck(report.RootElement, id);
            Assert.Equal("missing", fact.GetProperty("state").GetString());
            Assert.Equal("issue-not-published", fact.GetProperty("cause").GetString());
        }
    }

    [Fact]
    public void CanonicalPacketSourceExecutionUnitMismatchIsIdentityConflict()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(packetUnit: "G999", publishUnit: "G855");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.All(report.RootElement.GetProperty("steps").EnumerateArray(), step =>
        {
            Assert.Equal("unavailable", step.GetProperty("state").GetString());
            Assert.All(step.GetProperty("subchecks").EnumerateArray(), fact =>
            {
                Assert.Equal("unavailable", fact.GetProperty("state").GetString());
                Assert.Equal("identity-conflict", fact.GetProperty("cause").GetString());
                Assert.Equal("identity-conflict", fact.GetProperty("unavailable_class").GetString());
            });
        });
    }

    [Fact]
    public void ActualWrittenPinnedRulingIsObservedAndMissingRulingDoesNotFollowOrInventProof()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha256 = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha256);
        var beforeStatus = SnapshotFiles(host.Root);
        var reader = CreateKnownSixProvenanceLimitsReader();

        var firstOutput = RunStatusOutput(host, reader, "json", out var firstExit);
        Assert.Equal(0, firstExit);
        Assert.Equal(beforeStatus, SnapshotFiles(host.Root));
        using var first = JsonDocument.Parse(firstOutput);
        var firstRoot = first.RootElement;
        var firstFact = FindBugSource(firstRoot);
        var firstSource = firstFact.GetProperty("scope_sources");
        Assert.Equal("done", firstFact.GetProperty("state").GetString());
        AssertFactUnavailableClass(firstFact, null);
        Assert.Equal("scope-sources-satisfied", firstFact.GetProperty("cause").GetString());
        Assert.Equal("satisfied", firstSource.GetProperty("state").GetString());
        Assert.Equal("scope-sources-satisfied", firstSource.GetProperty("cause").GetString());
        Assert.Equal("supplied-not-authenticated", firstSource.GetProperty("authority_verification").GetString());
        Assert.Equal("not-verified", firstSource.GetProperty("publication").GetString());
        Assert.Equal(1, reader.ClaimReads);
        Assert.Equal(1, reader.GitHubReads);
        var observedAt = firstRoot.GetProperty("observation").GetProperty("observed_at").GetDateTimeOffset();
        Assert.Equal(RulingArtifact.FormatTimestamp(observedAt), firstSource.GetProperty("evaluated_at").GetString());
        var provenance = Assert.Single(firstSource.GetProperty("provenance").EnumerateArray());
        Assert.Equal(sha256, provenance.GetProperty("sha256").GetString());
        Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-G864-STATUS.json", provenance.GetProperty("path").GetString());
        Assert.Equal(G864ExpectedPublicBlock(sha256), firstSource.GetProperty("expected_provenance_block").GetString());
        Assert.False(provenance.TryGetProperty("canonical_record_json", out _));
        AssertKnownSixProvenanceLimits(firstRoot);

        var repeatedReader = CreateKnownSixProvenanceLimitsReader();
        var repeatedOutput = RunStatusOutput(host, repeatedReader, "json", out var repeatedExit);
        Assert.Equal(0, repeatedExit);
        Assert.Equal(1, repeatedReader.ClaimReads);
        Assert.Equal(1, repeatedReader.GitHubReads);
        using (var repeated = JsonDocument.Parse(repeatedOutput))
            Assert.Equal(SnapshotRenderedFacts(firstRoot, "bug-chain-or-ruling"),
                SnapshotRenderedFacts(repeated.RootElement, "bug-chain-or-ruling"));

        var markdownReader = CreateKnownSixProvenanceLimitsReader();
        var markdown = RunStatusOutput(host, markdownReader, "markdown", out var markdownExit);
        Assert.Equal(0, markdownExit);
        Assert.Equal(1, markdownReader.ClaimReads);
        Assert.Equal(1, markdownReader.GitHubReads);
        Assert.Contains("scope_sources:", markdown, StringComparison.Ordinal);
        Assert.Contains("state: `satisfied`", markdown, StringComparison.Ordinal);
        AssertMarkdownFactUnavailableClass(markdown, "bug-chain-or-ruling", null);
        Assert.Contains("authority_verification: `supplied-not-authenticated`", markdown, StringComparison.Ordinal);
        Assert.Contains("publication: `not-verified`", markdown, StringComparison.Ordinal);
        foreach (var line in G864ExpectedPublicBlock(sha256).Split('\n'))
            Assert.Contains("      " + line, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Keep the current bounded implementation choice", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("The current packet is within the declared scope", markdown, StringComparison.Ordinal);

        var rulingPath = Path.Combine(host.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-G864-STATUS.json");
        File.Delete(rulingPath);
        var afterRemoval = SnapshotFiles(host.Root);
        var missingReader = CreateKnownSixProvenanceLimitsReader();
        var missingOutput = RunStatusOutput(host, missingReader, "json", out var missingExit);
        Assert.Equal(0, missingExit);
        Assert.Equal(1, missingReader.ClaimReads);
        Assert.Equal(1, missingReader.GitHubReads);
        Assert.Equal(afterRemoval, SnapshotFiles(host.Root));
        using var missing = JsonDocument.Parse(missingOutput);
        var missingRoot = missing.RootElement;
        var missingFact = FindBugSource(missingRoot);
        var missingSource = missingFact.GetProperty("scope_sources");
        Assert.Equal("missing", missingFact.GetProperty("state").GetString());
        AssertFactUnavailableClass(missingFact, null);
        Assert.Equal("scope-sources-ruling-missing", missingFact.GetProperty("cause").GetString());
        Assert.Equal("refused", missingSource.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, missingSource.GetProperty("provenance").ValueKind);
        Assert.Null(missingSource.GetProperty("expected_provenance_block").GetString());
        Assert.Contains(missingSource.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("path").GetString() == ".intent-cli/rulings/intent-cli/intent-cli-dev/R-G864-STATUS.json");
        AssertKnownSixProvenanceLimits(missingRoot);
        Assert.Equal(SnapshotRenderedFacts(firstRoot, "bug-chain-or-ruling"),
            SnapshotRenderedFacts(missingRoot, "bug-chain-or-ruling"));

        var missingMarkdownReader = CreateKnownSixProvenanceLimitsReader();
        var missingMarkdown = RunStatusOutput(host, missingMarkdownReader, "markdown", out var missingMarkdownExit);
        Assert.Equal(0, missingMarkdownExit);
        Assert.Equal(1, missingMarkdownReader.ClaimReads);
        Assert.Equal(1, missingMarkdownReader.GitHubReads);
        Assert.Contains("state: `refused`", missingMarkdown, StringComparison.Ordinal);
        Assert.Contains("cause: `scope-sources-ruling-missing`", missingMarkdown, StringComparison.Ordinal);
        AssertMarkdownFactUnavailableClass(missingMarkdown, "bug-chain-or-ruling", null);
        Assert.DoesNotContain("sha256=", missingMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ActualWriterRepositoryAndExactUnitSourcesAreSortedInThePublicProof()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-two-sources-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        const string repositoryId = "R-G864-REPO";
        const string unitId = "R-G864-UNIT";
        var repositorySha = WriteActualG864Ruling(host, repositoryId, scopeKind: "repository", executionUnits: []);
        var unitSha = WriteActualG864Ruling(host, unitId);

        var directory = Path.Combine(host.Root, ".intent-cli", "issues", "G855");
        var packet = $$"""
            schema_version: 1
            implementation_issue_packet:
              source_execution_unit: G855
              domain: intent-cli
              team: intent-cli-dev
              target_repo: J-Tech-Japan/intent-system
            scope_sources:
              - "ruling:{{unitId}}"
              - "ruling:{{repositoryId}}"
            scope_source_digests:
              "ruling:{{unitId}}": "{{unitSha}}"
              "ruling:{{repositoryId}}": "{{repositorySha}}"
            """;
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), packet + "\n", new UTF8Encoding(false));
        var literalBlock = string.Join('\n',
        [
            "<!-- intent-cli:scope-sources:v1 -->",
            "### Ruling scope provenance",
            $"- ruling:{repositoryId} | domain=intent-cli | team=intent-cli-dev | repo=J-Tech-Japan/intent-system | unit=G855 | sha256={repositorySha}",
            $"- ruling:{unitId} | domain=intent-cli | team=intent-cli-dev | repo=J-Tech-Japan/intent-system | unit=G855 | sha256={unitSha}",
            "Authority: supplied-not-authenticated. Publication: not-verified.",
            "<!-- /intent-cli:scope-sources:v1 -->",
        ]);
        File.WriteAllText(Path.Combine(directory, "github-body.md"), "# G855 request\n\n" + literalBlock + "\n", new UTF8Encoding(false));

        var before = SnapshotFiles(host.Root);
        var reader = CreateKnownSixProvenanceLimitsReader();
        var json = RunStatusOutput(host, reader, "json", out var exit);
        Assert.Equal(0, exit);
        Assert.Equal(before, SnapshotFiles(host.Root));
        using var report = JsonDocument.Parse(json);
        var root = report.RootElement;
        var fact = FindBugSource(root);
        var source = fact.GetProperty("scope_sources");
        Assert.Equal("done", fact.GetProperty("state").GetString());
        AssertFactUnavailableClass(fact, null);
        Assert.Equal("scope-sources-satisfied", source.GetProperty("cause").GetString());
        Assert.Equal(RulingArtifact.FormatTimestamp(root.GetProperty("observation").GetProperty("observed_at").GetDateTimeOffset()),
            source.GetProperty("evaluated_at").GetString());
        Assert.Equal(literalBlock, source.GetProperty("expected_provenance_block").GetString());
        var provenance = source.GetProperty("provenance").EnumerateArray().ToArray();
        Assert.Equal(new[] { "ruling:" + repositoryId, "ruling:" + unitId },
            provenance.Select(item => item.GetProperty("reference").GetString()!).ToArray());
        Assert.Equal(new[] { repositorySha, unitSha }, provenance.Select(item => item.GetProperty("sha256").GetString()!).ToArray());
        Assert.All(provenance, item =>
        {
            Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/" + item.GetProperty("reference").GetString()![7..] + ".json",
                item.GetProperty("path").GetString());
            Assert.False(item.TryGetProperty("canonical_record_json", out _));
        });
        AssertKnownSixProvenanceLimits(root);

        var markdown = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), "markdown", out var markdownExit);
        Assert.Equal(0, markdownExit);
        AssertMarkdownFactUnavailableClass(markdown, "bug-chain-or-ruling", null);
        foreach (var line in literalBlock.Split('\n'))
            Assert.Contains("      " + line, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Keep the current bounded implementation choice", markdown, StringComparison.Ordinal);

        var repeated = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), "json", out var repeatedExit);
        Assert.Equal(0, repeatedExit);
        using var repeatedReport = JsonDocument.Parse(repeated);
        Assert.Equal(SnapshotRenderedFacts(root, "bug-chain-or-ruling"),
            SnapshotRenderedFacts(repeatedReport.RootElement, "bug-chain-or-ruling"));
    }

    [Fact]
    public void ActualExpiredAndSupersededPinsRemainInactiveUntilExplicitReplacement()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using (var expiredHost = new TempHost(Path.Combine(physicalTemp, "g864-status-expired-" + Guid.NewGuid().ToString("N"))))
        {
            expiredHost.WriteMode(TeamMode.SoloConductor);
            expiredHost.WritePacketAndPublishArtifact();
            const string expiredId = "R-G864-EXPIRED";
            var expiredSha = WriteActualG864Ruling(expiredHost, expiredId, expiresAt: G864RulingTime.AddHours(1));
            WriteG864SourcePacket(expiredHost, expiredSha, expiredId);
            var before = SnapshotFiles(expiredHost.Root);
            foreach (var format in new[] { "json", "markdown" })
            {
                var output = RunStatusOutput(expiredHost, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
                Assert.Equal(0, exit);
                Assert.Equal(before, SnapshotFiles(expiredHost.Root));
                if (format == "json")
                {
                    using var report = JsonDocument.Parse(output);
                    var fact = FindBugSource(report.RootElement);
                    var source = fact.GetProperty("scope_sources");
                    Assert.Equal("missing", fact.GetProperty("state").GetString());
                    AssertFactUnavailableClass(fact, null);
                    Assert.Equal("scope-sources-ruling-inactive", fact.GetProperty("cause").GetString());
                    Assert.Contains("expired", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
                    Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                        diagnostic.GetProperty("cause").GetString() == "ruling-expired");
                    Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
                    AssertKnownSixProvenanceLimits(report.RootElement);
                }
                else
                {
                    Assert.Contains("subcheck `bug-chain-or-ruling`: **missing**", output, StringComparison.Ordinal);
                    AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", null);
                    Assert.Contains("cause: `scope-sources-ruling-inactive`", output, StringComparison.Ordinal);
                    Assert.Contains("expired", output, StringComparison.Ordinal);
                    Assert.DoesNotContain("sha256=", output, StringComparison.Ordinal);
                    AssertKnownSixProvenanceLimitsInMarkdown(output);
                }
            }
        }

        using var supersededHost = new TempHost(Path.Combine(physicalTemp, "g864-status-superseded-" + Guid.NewGuid().ToString("N")));
        supersededHost.WriteMode(TeamMode.SoloConductor);
        supersededHost.WritePacketAndPublishArtifact();
        const string oldId = "R-G864-OLD";
        const string successorId = "R-G864-NEW";
        var oldSha = WriteActualG864Ruling(supersededHost, oldId);
        var successorSha = WriteActualG864Ruling(supersededHost, successorId,
            recordedAt: G864RulingTime.AddMinutes(1), supersedes: [oldId]);
        WriteG864SourcePacket(supersededHost, oldSha, oldId);
        var oldPinBytes = File.ReadAllBytes(Path.Combine(supersededHost.Root, ".intent-cli", "issues", "G855", "packet.yaml"));
        foreach (var format in new[] { "json", "markdown" })
        {
            var output = RunStatusOutput(supersededHost, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
            Assert.Equal(0, exit);
            Assert.Equal(oldPinBytes, File.ReadAllBytes(Path.Combine(supersededHost.Root, ".intent-cli", "issues", "G855", "packet.yaml")));
            if (format == "json")
            {
                using var report = JsonDocument.Parse(output);
                var fact = FindBugSource(report.RootElement);
                var source = fact.GetProperty("scope_sources");
                Assert.Equal("missing", fact.GetProperty("state").GetString());
                AssertFactUnavailableClass(fact, null);
                Assert.Equal("scope-sources-ruling-inactive", fact.GetProperty("cause").GetString());
                Assert.Contains("superseded", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
                Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                    diagnostic.GetProperty("cause").GetString() == "ruling-superseded");
                Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
                Assert.DoesNotContain(successorId, output, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("subcheck `bug-chain-or-ruling`: **missing**", output, StringComparison.Ordinal);
                AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", null);
                Assert.Contains("cause: `scope-sources-ruling-inactive`", output, StringComparison.Ordinal);
                Assert.Contains("superseded", output, StringComparison.Ordinal);
                Assert.DoesNotContain("sha256=", output, StringComparison.Ordinal);
                Assert.DoesNotContain(successorId, output, StringComparison.Ordinal);
            }
        }

        WriteG864SourcePacket(supersededHost, successorSha, successorId);
        foreach (var format in new[] { "json", "markdown" })
        {
            var output = RunStatusOutput(supersededHost, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
            Assert.Equal(0, exit);
            if (format == "json")
            {
                using var report = JsonDocument.Parse(output);
                var fact = FindBugSource(report.RootElement);
                Assert.Equal("done", fact.GetProperty("state").GetString());
                Assert.Equal(successorSha, Assert.Single(fact.GetProperty("scope_sources").GetProperty("provenance").EnumerateArray())
                    .GetProperty("sha256").GetString());
            }
            else
            {
                Assert.Contains("subcheck `bug-chain-or-ruling`: **done**", output, StringComparison.Ordinal);
                Assert.Contains("sha256=" + successorSha, output, StringComparison.Ordinal);
            }
        }
    }

    public static IEnumerable<object[]> G864RulingMutationCases()
    {
        var cases = new (string Name, string Cause, string? Diagnostic, string? UnavailableClass)[ ]
        {
            ("canonical-content-changed", "scope-sources-digest-mismatch", null, "identity-conflict"),
            ("malformed-json", "scope-sources-ruling-unavailable", "ruling-invalid-json", "read-failure"),
            ("literal-null", "scope-sources-ruling-unavailable", "ruling-invalid-input", "read-failure"),
            ("trailing-whitespace", "scope-sources-ruling-unavailable", "ruling-noncanonical-artifact", "read-failure"),
        };
        foreach (var item in cases)
        foreach (var format in new[] { "json", "markdown" })
            yield return [item.Name, item.Cause, item.Diagnostic ?? "", item.UnavailableClass ?? "", format];
    }

    [Theory]
    [MemberData(nameof(G864RulingMutationCases))]
    public void ActualPinnedArtifactReplacementProducesDistinctReadOrDigestResult(string scenario, string expectedCause,
        string expectedDiagnostic, string expectedUnavailableClass, string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-mutated-ruling-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        const string id = "R-G864-MUTATION";
        var pinnedSha = WriteActualG864Ruling(host, id);
        WriteG864SourcePacket(host, pinnedSha, id);
        var artifactPath = Path.Combine(host.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", id + ".json");
        var originalBytes = File.ReadAllBytes(artifactPath);
        using var baselineReport = RunStatusJson(host, CreateKnownSixProvenanceLimitsReader(), out var baselineExit);
        Assert.Equal(0, baselineExit);
        var baselineOtherFacts = SnapshotRenderedFacts(baselineReport.RootElement, "bug-chain-or-ruling");
        Assert.Equal(25, baselineOtherFacts.Count);
        byte[] replacementBytes;
        switch (scenario)
        {
            case "canonical-content-changed":
                Assert.True(RulingArtifact.TryParse(originalBytes, requireRecordedAt: true, out var original, out _, out _));
                replacementBytes = RulingArtifact.Serialize(original! with
                {
                    Decision = "Canonical bytes changed after the packet was pinned.",
                });
                Assert.NotEqual(originalBytes, replacementBytes);
                Assert.True(RulingArtifact.TryParse(replacementBytes, requireRecordedAt: true, out _, out _, out _));
                break;
            case "malformed-json":
                replacementBytes = Encoding.UTF8.GetBytes("{\n");
                break;
            case "literal-null":
                replacementBytes = Encoding.UTF8.GetBytes("null\n");
                break;
            case "trailing-whitespace":
                replacementBytes = originalBytes.Concat(Encoding.UTF8.GetBytes(" \n")).ToArray();
                using (var parsed = JsonDocument.Parse(replacementBytes))
                    Assert.Equal("R-G864-MUTATION", parsed.RootElement.GetProperty("id").GetString());
                break;
            default:
                throw new InvalidOperationException("Unrecognized G864 mutation: " + scenario);
        }
        File.WriteAllBytes(artifactPath, replacementBytes);
        Assert.Equal(replacementBytes, File.ReadAllBytes(artifactPath));
        var beforeStatus = SnapshotFiles(host.Root);

        var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(beforeStatus, SnapshotFiles(host.Root));
        JsonDocument afterFactsReport;
        if (format == "json")
        {
            afterFactsReport = JsonDocument.Parse(output);
        }
        else
        {
            afterFactsReport = RunStatusJson(host, CreateKnownSixProvenanceLimitsReader(), out var comparisonExit);
            Assert.Equal(1, comparisonExit);
        }
        using (afterFactsReport)
        {
            var afterOtherFacts = SnapshotRenderedFacts(afterFactsReport.RootElement, "bug-chain-or-ruling");
            Assert.Equal(25, afterOtherFacts.Count);
            Assert.Equal(baselineOtherFacts, afterOtherFacts);
        }
        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var root = report.RootElement;
            var fact = FindBugSource(root);
            var source = fact.GetProperty("scope_sources");
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal(expectedCause, fact.GetProperty("cause").GetString());
            AssertFactUnavailableClass(fact, expectedUnavailableClass.Length == 0 ? null : expectedUnavailableClass);
            Assert.Equal(expectedCause, source.GetProperty("cause").GetString());
            Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
            Assert.Equal(JsonValueKind.Null, source.GetProperty("expected_provenance_block").ValueKind);
            AssertKnownSixProvenanceLimits(root, expectedObservationExitCode: 1);
            if (expectedDiagnostic.Length > 0)
                Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                    diagnostic.GetProperty("cause").GetString() == expectedDiagnostic
                    && diagnostic.GetProperty("path").GetString() == ".intent-cli/rulings/intent-cli/intent-cli-dev/" + id + ".json");
        }
        else
        {
            Assert.Contains("subcheck `bug-chain-or-ruling`: **unavailable**", output, StringComparison.Ordinal);
            Assert.Contains("cause: `" + expectedCause + "`", output, StringComparison.Ordinal);
            AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", expectedUnavailableClass.Length == 0 ? null : expectedUnavailableClass);
            AssertKnownSixProvenanceLimitsInMarkdown(output);
            Assert.DoesNotContain("sha256=", output, StringComparison.Ordinal);
            if (expectedDiagnostic.Length > 0)
            {
                Assert.Contains(expectedDiagnostic, output, StringComparison.Ordinal);
                Assert.Contains(".intent-cli/rulings/intent-cli/intent-cli-dev/" + id + ".json", output, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void UnreadablePinnedArtifactPreservesOtherFactsAndReportsItsReachedPath(string format)
    {
        if (OperatingSystem.IsWindows())
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture requires Unix file modes.");
        if (!G841TestHelpers.IsNonRootUnixUser())
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture cannot prove denial while running as root.");

        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-ruling-denied-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha256 = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha256);
        var artifactPath = Path.Combine(host.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-G864-STATUS.json");
        const string relativeArtifactPath = ".intent-cli/rulings/intent-cli/intent-cli-dev/R-G864-STATUS.json";
        var originalBytes = File.ReadAllBytes(artifactPath);
        var originalMode = File.GetUnixFileMode(artifactPath);
        using var baselineReport = RunStatusJson(host, CreateKnownSixProvenanceLimitsReader(), out var baselineExit);
        Assert.Equal(0, baselineExit);
        var baselineOtherFacts = SnapshotRenderedFacts(baselineReport.RootElement, "bug-chain-or-ruling");
        Assert.Equal(25, baselineOtherFacts.Count);

        try
        {
            File.SetUnixFileMode(artifactPath, UnixFileMode.None);
            try
            {
                _ = File.ReadAllBytes(artifactPath);
                throw Xunit.Sdk.SkipException.ForSkip("The operating system did not reject direct reads of the mode-000 ruling fixture.");
            }
            catch (UnauthorizedAccessException)
            {
                // The fixture reached the real pinned-artifact read boundary.
            }

            var before = SnapshotFiles(host.Root, relativeArtifactPath);
            var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
            Assert.Equal(1, exit);
            Assert.Equal(before, SnapshotFiles(host.Root, relativeArtifactPath));
            JsonDocument comparisonReport;
            if (format == "json")
            {
                comparisonReport = JsonDocument.Parse(output);
            }
            else
            {
                comparisonReport = RunStatusJson(host, CreateKnownSixProvenanceLimitsReader(), out var comparisonExit);
                Assert.Equal(1, comparisonExit);
            }
            using (comparisonReport)
            {
                var root = comparisonReport.RootElement;
                var otherFacts = SnapshotRenderedFacts(root, "bug-chain-or-ruling");
                Assert.Equal(25, otherFacts.Count);
                Assert.Equal(baselineOtherFacts, otherFacts);
                var fact = FindBugSource(root);
                Assert.Equal("unavailable", fact.GetProperty("state").GetString());
                Assert.Equal("read-failure", fact.GetProperty("unavailable_class").GetString());
                Assert.Equal("scope-sources-ruling-unavailable", fact.GetProperty("cause").GetString());
                var source = fact.GetProperty("scope_sources");
                Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
                Assert.Equal(JsonValueKind.Null, source.GetProperty("expected_provenance_block").ValueKind);
                Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                    diagnostic.GetProperty("cause").GetString() == "ruling-artifact-unavailable"
                    && diagnostic.GetProperty("path").GetString() == relativeArtifactPath);
                AssertKnownSixProvenanceLimits(root, expectedObservationExitCode: 1);
            }
            if (format == "markdown")
            {
                AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
                Assert.Contains("cause: `scope-sources-ruling-unavailable`", output, StringComparison.Ordinal);
                Assert.Contains("ruling-artifact-unavailable", output, StringComparison.Ordinal);
                Assert.Contains("path=`" + relativeArtifactPath + "`", output, StringComparison.Ordinal);
                Assert.DoesNotContain("provenance: reference=", output, StringComparison.Ordinal);
                AssertKnownSixProvenanceLimitsInMarkdown(output);
            }
        }
        finally
        {
            File.SetUnixFileMode(artifactPath, originalMode);
        }
        Assert.Equal(originalBytes, File.ReadAllBytes(artifactPath));
    }

    public static IEnumerable<object[]> G864BodyBoundaryCases()
    {
        foreach (var item in new[] { ("missing", "Missing"), ("empty", "Empty") })
        foreach (var format in new[] { "json", "markdown" })
            yield return [item.Item1, item.Item2, format];
    }

    [Theory]
    [MemberData(nameof(G864BodyBoundaryCases))]
    public void MissingAndEmptyPublicBodyAreReadFailuresRatherThanProvenanceMismatches(string mutation,
        string expectedFailure, string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-body-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha256 = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha256);
        var bodyPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "github-body.md");
        if (mutation == "missing") File.Delete(bodyPath);
        else File.WriteAllBytes(bodyPath, []);
        var changedBytes = SnapshotFiles(host.Root);

        var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(changedBytes, SnapshotFiles(host.Root));
        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var fact = FindBugSource(report.RootElement);
            var source = fact.GetProperty("scope_sources");
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, "read-failure");
            Assert.Equal("scope-sources-packet-unavailable", fact.GetProperty("cause").GetString());
            Assert.Equal("scope-sources-packet-unavailable", source.GetProperty("cause").GetString());
            Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                diagnostic.GetProperty("cause").GetString() == "packet-read-failed"
                && diagnostic.GetProperty("path").GetString() == ".intent-cli/issues/G855/github-body.md"
                && diagnostic.GetProperty("detail").GetString()!.Contains(expectedFailure, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
            Assert.Equal(JsonValueKind.Null, source.GetProperty("expected_provenance_block").ValueKind);
            AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
        }
        else
        {
            Assert.Contains("subcheck `bug-chain-or-ruling`: **unavailable**", output, StringComparison.Ordinal);
            AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
            Assert.Contains("cause: `scope-sources-packet-unavailable`", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/issues/G855/github-body.md", output, StringComparison.Ordinal);
            Assert.Contains(expectedFailure, output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expected_provenance_block:", output, StringComparison.Ordinal);
            AssertKnownSixProvenanceLimitsInMarkdown(output);
        }
    }

    [Fact]
    public void UnreadablePublicBodyIsReportedWithItsExactMemberPath()
    {
        if (OperatingSystem.IsWindows())
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture requires Unix file modes.");
        if (!G841TestHelpers.IsNonRootUnixUser())
            throw Xunit.Sdk.SkipException.ForSkip("Unix permission-denied fixture cannot prove denial while running as root.");

        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-body-denied-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha256 = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha256);
        var bodyPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "github-body.md");
        var originalMode = File.GetUnixFileMode(bodyPath);
        var before = SnapshotFiles(host.Root);
        try
        {
            File.SetUnixFileMode(bodyPath, UnixFileMode.None);
            try
            {
                _ = File.ReadAllBytes(bodyPath);
                throw Xunit.Sdk.SkipException.ForSkip("The operating system did not reject direct reads of the mode-000 body fixture.");
            }
            catch (UnauthorizedAccessException)
            {
                // The fixture has reached the real permission-denied boundary.
            }

            Assert.False(CrossRuntimeReviewFileMode.TryReadRegularFileBytes(bodyPath, out _, out var failure, out _));
            Assert.Equal(CrossRuntimeReviewFileReadFailure.ReadError, failure);
            foreach (var format in new[] { "json", "markdown" })
            {
                var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
                Assert.Equal(1, exit);
                if (format == "json")
                {
                    using var report = JsonDocument.Parse(output);
                    var fact = FindBugSource(report.RootElement);
                    var source = fact.GetProperty("scope_sources");
                    Assert.Equal("unavailable", fact.GetProperty("state").GetString());
                    AssertFactUnavailableClass(fact, "read-failure");
                    Assert.Equal("scope-sources-packet-unavailable", source.GetProperty("cause").GetString());
                    Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                        diagnostic.GetProperty("path").GetString() == ".intent-cli/issues/G855/github-body.md");
                    AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
                }
                else
                {
                    AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
                    Assert.Contains("cause: `scope-sources-packet-unavailable`", output, StringComparison.Ordinal);
                    Assert.Contains(".intent-cli/issues/G855/github-body.md", output, StringComparison.Ordinal);
                    AssertKnownSixProvenanceLimitsInMarkdown(output);
                }
            }
        }
        finally
        {
            File.SetUnixFileMode(bodyPath, originalMode);
        }
        Assert.Equal(before, SnapshotFiles(host.Root));
    }

    public static IEnumerable<object[]> G864InvalidDeclarationCases()
    {
        var cases = new[]
        {
            "null-list", "scalar-list", "mapping-list", "mapping-reference", "missing-pin", "extra-pin",
            "null-pin", "duplicate-reference", "case-alias-reference", "tagged-list", "participating-alias",
        };
        foreach (var scenario in cases)
        foreach (var format in new[] { "json", "markdown" })
            yield return [scenario, format];
    }

    [Theory]
    [MemberData(nameof(G864InvalidDeclarationCases))]
    public void ActualSourceConsumerRefusesMalformedDeclarationShapes(string scenario, string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-invalid-source-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        const string id = "R-G864-INVALID";
        var sha256 = WriteActualG864Ruling(host, id);
        WriteG864SourcePacket(host, sha256, id);
        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        var original = File.ReadAllText(packetPath);
        var reference = "ruling:" + id;
        var marker = "scope_sources:\n  - \"" + reference + "\"\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"";
        Assert.Contains(marker, original, StringComparison.Ordinal);
        var replacement = scenario switch
        {
            "null-list" => "scope_sources: null\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"",
            "scalar-list" => "scope_sources: \"" + reference + "\"\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"",
            "mapping-list" => "scope_sources: { reference: \"" + reference + "\" }\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"",
            "mapping-reference" => "scope_sources:\n  - { reference: \"" + reference + "\" }\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"",
            "missing-pin" => "scope_sources:\n  - \"" + reference + "\"",
            "extra-pin" => "scope_sources:\n  - \"" + reference + "\"\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"\n  \"ruling:R-G864-EXTRA\": \"" + new string('a', 64) + "\"",
            "null-pin" => "scope_sources:\n  - \"" + reference + "\"\nscope_source_digests:\n  \"" + reference + "\": null",
            "duplicate-reference" => "scope_sources:\n  - \"" + reference + "\"\n  - \"" + reference + "\"\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"",
            "case-alias-reference" => "scope_sources:\n  - \"" + reference + "\"\n  - \"ruling:r-g864-invalid\"\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"\n  \"ruling:r-g864-invalid\": \"" + new string('a', 64) + "\"",
            "tagged-list" => "scope_sources: !unsupported []\nscope_source_digests: {}",
            "participating-alias" => "source_ref: &source \"" + reference + "\"\nscope_sources:\n  - *source\nscope_source_digests:\n  \"" + reference + "\": \"" + sha256 + "\"",
            _ => throw new InvalidOperationException("Unrecognized G864 invalid declaration: " + scenario),
        };
        var changed = original.Replace(marker, replacement, StringComparison.Ordinal);
        Assert.NotEqual(original, changed);
        File.WriteAllText(packetPath, changed, new UTF8Encoding(false));
        var changedBytes = File.ReadAllBytes(packetPath);
        Assert.Equal(Encoding.UTF8.GetBytes(changed), changedBytes);
        var before = SnapshotFiles(host.Root);

        var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(before, SnapshotFiles(host.Root));
        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var fact = FindBugSource(report.RootElement);
            var source = fact.GetProperty("scope_sources");
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, "read-failure");
            Assert.Equal("scope-sources-invalid-declaration", fact.GetProperty("cause").GetString());
            Assert.Equal("scope-sources-invalid-declaration", source.GetProperty("cause").GetString());
            Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
            Assert.Equal(JsonValueKind.Null, source.GetProperty("expected_provenance_block").ValueKind);
            AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
        }
        else
        {
            Assert.Contains("subcheck `bug-chain-or-ruling`: **unavailable**", output, StringComparison.Ordinal);
            AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
            Assert.Contains("cause: `scope-sources-invalid-declaration`", output, StringComparison.Ordinal);
            Assert.DoesNotContain("sha256=", output, StringComparison.Ordinal);
            AssertKnownSixProvenanceLimitsInMarkdown(output);
        }
    }

    [Theory]
    [InlineData("multidocument", "json")]
    [InlineData("multidocument", "markdown")]
    [InlineData("duplicate-root-key", "json")]
    [InlineData("duplicate-root-key", "markdown")]
    public void OptedInMultiDocumentAndDuplicateMappingPacketsFailClosedAtTheConsumerBoundary(string scenario, string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-packet-shape-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha = WriteActualG864Ruling(host, "R-G864-PACKET-SHAPE");
        WriteG864SourcePacket(host, sha, "R-G864-PACKET-SHAPE");
        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        var validPacket = File.ReadAllText(packetPath);
        var valid = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), "json", out var validExit);
        Assert.Equal(0, validExit);
        using (var validReport = JsonDocument.Parse(valid))
            Assert.Equal("done", FindBugSource(validReport.RootElement).GetProperty("state").GetString());

        var mutatedPacket = scenario switch
        {
            "multidocument" => validPacket + "---\nmetadata: second-document\n",
            "duplicate-root-key" => validPacket + "scope_sources: []\n",
            _ => throw new InvalidOperationException("Unknown packet shape: " + scenario),
        };
        Assert.NotEqual(validPacket, mutatedPacket);
        File.WriteAllText(packetPath, mutatedPacket, new UTF8Encoding(false));
        var changedBytes = File.ReadAllBytes(packetPath);
        Assert.Equal(Encoding.UTF8.GetBytes(mutatedPacket), changedBytes);
        var before = SnapshotFiles(host.Root);

        var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(before, SnapshotFiles(host.Root));
        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var root = report.RootElement;
            var fact = FindBugSource(root);
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            Assert.Equal("read-failure", fact.GetProperty("unavailable_class").GetString());
            if (scenario == "multidocument")
            {
                var source = fact.GetProperty("scope_sources");
                Assert.Equal("unavailable", source.GetProperty("state").GetString());
                Assert.Equal("scope-sources-packet-unavailable", source.GetProperty("cause").GetString());
                Assert.Equal("scope-sources-packet-unavailable", fact.GetProperty("cause").GetString());
                Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
                Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                    diagnostic.GetProperty("cause").GetString() == "packet-unparseable");
                AssertKnownSixProvenanceLimits(root, expectedObservationExitCode: 1);
            }
            else
            {
                Assert.Equal("packet-identity-unreadable", fact.GetProperty("cause").GetString());
                Assert.False(fact.TryGetProperty("scope_sources", out _));
                Assert.Equal("packet-identity-unreadable", root.GetProperty("applicability").GetProperty("cause").GetString());
                Assert.Equal("read-failure", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
                var priorProvenanceFacts = root.GetProperty("steps").EnumerateArray()
                    .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
                    .Where(item => item.TryGetProperty("unavailable_class", out var value)
                        && value.GetString() == "provenance-limit")
                    .Select(item => item.GetProperty("id").GetString())
                    .OrderBy(item => item, StringComparer.Ordinal).ToArray();
                Assert.Equal(new[] { "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release" },
                    priorProvenanceFacts);
            }
        }
        else
        {
            AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
            if (scenario == "multidocument")
            {
                Assert.Contains("cause: `scope-sources-packet-unavailable`", output, StringComparison.Ordinal);
                Assert.Contains("scope_sources:", output, StringComparison.Ordinal);
                Assert.DoesNotContain("sha256=", output, StringComparison.Ordinal);
                AssertKnownSixProvenanceLimitsInMarkdown(output);
            }
            else
            {
                Assert.Contains("cause: `packet-identity-unreadable`", output, StringComparison.Ordinal);
                Assert.DoesNotContain("scope_sources:", output, StringComparison.Ordinal);
                Assert.Contains("packet-identity-unreadable", output, StringComparison.Ordinal);
                Assert.Contains("read-failure", output, StringComparison.Ordinal);
                Assert.Equal(4, output.Split("unavailable_class: `provenance-limit`", StringSplitOptions.None).Length - 1);
            }
        }
    }

    [Fact]
    public void AbsentAndExplicitEmptySourceDeclarationsKeepLegacyOutputAndReaderBudgets()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-empty-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(rulingArtifact: ".intent-cli/rulings/G855.yaml");
        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        var baselineJsonReader = CreateKnownSixProvenanceLimitsReader();
        var baselineJson = RunStatusOutput(host, baselineJsonReader, "json", out var baselineJsonExit);
        Assert.Equal(0, baselineJsonExit);
        using (var baseline = JsonDocument.Parse(baselineJson))
        {
            var source = FindBugSource(baseline.RootElement);
            Assert.Equal("ruling-reference-unsupported", source.GetProperty("cause").GetString());
            Assert.False(source.TryGetProperty("scope_sources", out _));
        }
        var baselineMarkdownReader = CreateKnownSixProvenanceLimitsReader();
        var baselineMarkdown = RunStatusOutput(host, baselineMarkdownReader, "markdown", out var baselineMarkdownExit);
        Assert.Equal(0, baselineMarkdownExit);
        Assert.DoesNotContain("scope_sources:", baselineMarkdown, StringComparison.Ordinal);

        File.AppendAllText(packetPath, "scope_sources: []\nscope_source_digests: {}\n", new UTF8Encoding(false));
        var emptyJsonReader = CreateKnownSixProvenanceLimitsReader();
        var emptyJson = RunStatusOutput(host, emptyJsonReader, "json", out var emptyJsonExit);
        Assert.Equal(0, emptyJsonExit);
        using (var empty = JsonDocument.Parse(emptyJson))
        {
            var source = FindBugSource(empty.RootElement);
            Assert.Equal("ruling-reference-unsupported", source.GetProperty("cause").GetString());
            Assert.False(source.TryGetProperty("scope_sources", out _));
        }
        Assert.Equal(NormalizeStatusObservedAt(baselineJson), NormalizeStatusObservedAt(emptyJson));
        Assert.Equal(baselineJsonReader.ClaimReads, emptyJsonReader.ClaimReads);
        Assert.Equal(baselineJsonReader.GitHubReads, emptyJsonReader.GitHubReads);

        var emptyMarkdownReader = CreateKnownSixProvenanceLimitsReader();
        var emptyMarkdown = RunStatusOutput(host, emptyMarkdownReader, "markdown", out var emptyMarkdownExit);
        Assert.Equal(0, emptyMarkdownExit);
        Assert.Equal(NormalizeMarkdownObservedAt(baselineMarkdown), NormalizeMarkdownObservedAt(emptyMarkdown));
        Assert.Equal(baselineMarkdownReader.ClaimReads, emptyMarkdownReader.ClaimReads);
        Assert.Equal(baselineMarkdownReader.GitHubReads, emptyMarkdownReader.GitHubReads);
        Assert.DoesNotContain("scope_sources:", emptyMarkdown, StringComparison.Ordinal);

        var absentWithReplacementCharacter = File.ReadAllText(packetPath)
            .Replace("scope_sources: []\nscope_source_digests: {}\n", string.Empty, StringComparison.Ordinal)
            + "legacy_note: \"\uFFFD\"\n";
        File.WriteAllText(packetPath, absentWithReplacementCharacter, new UTF8Encoding(false));
        var replacementReader = CreateKnownSixProvenanceLimitsReader();
        var replacementJson = RunStatusOutput(host, replacementReader, "json", out var replacementExit);
        Assert.Equal(0, replacementExit);
        using var replacement = JsonDocument.Parse(replacementJson);
        var replacementFact = FindBugSource(replacement.RootElement);
        Assert.Equal("ruling-reference-unsupported", replacementFact.GetProperty("cause").GetString());
        Assert.False(replacementFact.TryGetProperty("scope_sources", out _));
        using var baselineAgain = JsonDocument.Parse(baselineJson);
        Assert.Equal(FindBugSource(baselineAgain.RootElement).GetRawText(), replacementFact.GetRawText());
        Assert.Equal(NormalizeStatusObservedAt(baselineJson), NormalizeStatusObservedAt(replacementJson));
        Assert.Equal(baselineJsonReader.ClaimReads, replacementReader.ClaimReads);
        Assert.Equal(baselineJsonReader.GitHubReads, replacementReader.GitHubReads);

        var replacementMarkdownReader = CreateKnownSixProvenanceLimitsReader();
        var replacementMarkdown = RunStatusOutput(host, replacementMarkdownReader, "markdown", out var replacementMarkdownExit);
        Assert.Equal(0, replacementMarkdownExit);
        Assert.Equal(NormalizeMarkdownObservedAt(baselineMarkdown), NormalizeMarkdownObservedAt(replacementMarkdown));
        Assert.Equal(baselineMarkdownReader.ClaimReads, replacementMarkdownReader.ClaimReads);
        Assert.Equal(baselineMarkdownReader.GitHubReads, replacementMarkdownReader.GitHubReads);
        Assert.DoesNotContain("scope_sources:", replacementMarkdown, StringComparison.Ordinal);

        var bodyPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "github-body.md");
        File.Delete(bodyPath);
        var packetWithReplacement = File.ReadAllText(packetPath);
        var packetWithoutReplacement = packetWithReplacement.Replace("legacy_note: \"\uFFFD\"\n", string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(packetWithReplacement, packetWithoutReplacement);

        File.WriteAllText(packetPath, packetWithoutReplacement, new UTF8Encoding(false));
        var missingBodyBaselineJsonReader = CreateKnownSixProvenanceLimitsReader();
        var missingBodyBaselineJson = RunStatusOutput(host, missingBodyBaselineJsonReader, "json", out var missingBodyBaselineJsonExit);
        Assert.Equal(0, missingBodyBaselineJsonExit);
        var missingBodyBaselineMarkdownReader = CreateKnownSixProvenanceLimitsReader();
        var missingBodyBaselineMarkdown = RunStatusOutput(host, missingBodyBaselineMarkdownReader, "markdown", out var missingBodyBaselineMarkdownExit);
        Assert.Equal(0, missingBodyBaselineMarkdownExit);

        File.WriteAllText(packetPath, packetWithReplacement, new UTF8Encoding(false));
        var missingBodyReplacementJsonReader = CreateKnownSixProvenanceLimitsReader();
        var missingBodyReplacementJson = RunStatusOutput(host, missingBodyReplacementJsonReader, "json", out var missingBodyReplacementJsonExit);
        Assert.Equal(0, missingBodyReplacementJsonExit);
        var missingBodyReplacementMarkdownReader = CreateKnownSixProvenanceLimitsReader();
        var missingBodyReplacementMarkdown = RunStatusOutput(host, missingBodyReplacementMarkdownReader, "markdown", out var missingBodyReplacementMarkdownExit);
        Assert.Equal(0, missingBodyReplacementMarkdownExit);

        Assert.Equal(NormalizeStatusObservedAt(missingBodyBaselineJson), NormalizeStatusObservedAt(missingBodyReplacementJson));
        Assert.Equal(NormalizeMarkdownObservedAt(missingBodyBaselineMarkdown), NormalizeMarkdownObservedAt(missingBodyReplacementMarkdown));
        Assert.Equal(missingBodyBaselineJsonReader.ClaimReads, missingBodyReplacementJsonReader.ClaimReads);
        Assert.Equal(missingBodyBaselineJsonReader.GitHubReads, missingBodyReplacementJsonReader.GitHubReads);
        Assert.Equal(missingBodyBaselineMarkdownReader.ClaimReads, missingBodyReplacementMarkdownReader.ClaimReads);
        Assert.Equal(missingBodyBaselineMarkdownReader.GitHubReads, missingBodyReplacementMarkdownReader.GitHubReads);
        using var missingBodyReplacementReport = JsonDocument.Parse(missingBodyReplacementJson);
        var missingBodyReplacementFact = FindBugSource(missingBodyReplacementReport.RootElement);
        Assert.Equal("ruling-reference-unsupported", missingBodyReplacementFact.GetProperty("cause").GetString());
        Assert.False(missingBodyReplacementFact.TryGetProperty("scope_sources", out _));
        Assert.DoesNotContain("scope_sources:", missingBodyReplacementMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("provenance:", missingBodyReplacementMarkdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void InvalidUtf8InOtherwiseValidPacketFailsClosedThroughTheRawByteEvaluator(string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-invalid-utf8-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha256 = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha256);
        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        var validPacketBytes = File.ReadAllBytes(packetPath);
        var invalidBytes = validPacketBytes.Concat(Encoding.UTF8.GetBytes("# invalid utf8: ")).Concat([byte.MaxValue, (byte)'\n']).ToArray();
        File.WriteAllBytes(packetPath, invalidBytes);
        Assert.Equal(invalidBytes, File.ReadAllBytes(packetPath));
        var before = SnapshotFiles(host.Root);

        var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(before, SnapshotFiles(host.Root));
        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var fact = FindBugSource(report.RootElement);
            var source = fact.GetProperty("scope_sources");
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, "read-failure");
            Assert.Equal("scope-sources-packet-unavailable", source.GetProperty("cause").GetString());
            Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                diagnostic.GetProperty("cause").GetString() == "packet-unparseable"
                && diagnostic.GetProperty("path").ValueKind == JsonValueKind.Null
                && diagnostic.GetProperty("detail").GetString()!.Contains("FF", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
            AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
        }
        else
        {
            AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
            Assert.Contains("cause: `scope-sources-packet-unavailable`", output, StringComparison.Ordinal);
            Assert.Contains("packet-unparseable", output, StringComparison.Ordinal);
            Assert.Contains("Unable to translate bytes", output, StringComparison.Ordinal);
            AssertKnownSixProvenanceLimitsInMarkdown(output);
        }
    }

    [Fact]
    public void StructuralSourcesTakePrecedenceOverLegacyBugSourcesAndDoNotScanUnrelatedReports()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-precedence-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G864-VALID", SourceIssueUrl);
        var legacyBefore = SnapshotFiles(host.Root);
        foreach (var format in new[] { "json", "markdown" })
        {
            var legacyReader = CreateKnownSixProvenanceLimitsReader();
            var legacyOutput = RunStatusOutput(host, legacyReader, format, out var legacyExit);
            Assert.Equal(0, legacyExit);
            Assert.Equal(legacyBefore, SnapshotFiles(host.Root));
            Assert.Equal(1, legacyReader.ClaimReads);
            Assert.Equal(1, legacyReader.GitHubReads);
            if (format == "json")
            {
                using var report = JsonDocument.Parse(legacyOutput);
                var fact = FindBugSource(report.RootElement);
                Assert.Equal("done", fact.GetProperty("state").GetString());
                Assert.Equal("canonical-source-chain-recorded", fact.GetProperty("cause").GetString());
                Assert.False(fact.TryGetProperty("scope_sources", out _));
                AssertKnownSixProvenanceLimits(report.RootElement);
            }
            else
            {
                Assert.Contains("subcheck `bug-chain-or-ruling`: **done**", legacyOutput, StringComparison.Ordinal);
                Assert.Contains("canonical-source-chain-recorded", legacyOutput, StringComparison.Ordinal);
                Assert.DoesNotContain("scope_sources:", legacyOutput, StringComparison.Ordinal);
                AssertKnownSixProvenanceLimitsInMarkdown(legacyOutput);
            }
        }

        var sha256 = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, new string('0', 64), sourceArtifact: SourceIssueUrl);

        foreach (var format in new[] { "json", "markdown" })
        {
            var reader = CreateKnownSixProvenanceLimitsReader();
            var output = RunStatusOutput(host, reader, format, out var exit);
            Assert.Equal(1, exit);
            Assert.Equal(1, reader.ClaimReads);
            Assert.Equal(1, reader.GitHubReads);
            if (format == "json")
            {
                using var report = JsonDocument.Parse(output);
                var fact = FindBugSource(report.RootElement);
                Assert.Equal("unavailable", fact.GetProperty("state").GetString());
                AssertFactUnavailableClass(fact, "identity-conflict");
                Assert.Equal("scope-sources-digest-mismatch", fact.GetProperty("cause").GetString());
                Assert.Equal(JsonValueKind.Null, fact.GetProperty("scope_sources").GetProperty("provenance").ValueKind);
                AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
            }
            else
            {
                AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "identity-conflict");
                Assert.Contains("cause: `scope-sources-digest-mismatch`", output, StringComparison.Ordinal);
                Assert.DoesNotContain("source-chain-satisfied", output, StringComparison.Ordinal);
                Assert.DoesNotContain("sha256=" + sha256, output, StringComparison.Ordinal);
                AssertKnownSixProvenanceLimitsInMarkdown(output);
            }
        }

        WriteG864SourcePacket(host, sha256, sourceArtifact: SourceIssueUrl);
        var bugs = Path.Combine(host.Root, ".intent-cli", "bugs");
        Directory.CreateDirectory(bugs);
        var malformedUnrelated = Path.Combine(bugs, "AAA-UNRELATED.report.yaml");
        File.WriteAllText(malformedUnrelated, "not: [valid bug report yaml\n", new UTF8Encoding(false));
        var before = SnapshotFiles(host.Root);
        foreach (var format in new[] { "json", "markdown" })
        {
            var reader = CreateKnownSixProvenanceLimitsReader();
            var output = RunStatusOutput(host, reader, format, out var exit);
            Assert.Equal(0, exit);
            Assert.Equal(1, reader.ClaimReads);
            Assert.Equal(1, reader.GitHubReads);
            Assert.Equal(before, SnapshotFiles(host.Root));
            if (format == "json")
            {
                using var report = JsonDocument.Parse(output);
                var fact = FindBugSource(report.RootElement);
                Assert.Equal("done", fact.GetProperty("state").GetString());
                AssertFactUnavailableClass(fact, null);
                Assert.Equal("scope-sources-satisfied", fact.GetProperty("cause").GetString());
                Assert.Equal(sha256, Assert.Single(fact.GetProperty("scope_sources").GetProperty("provenance").EnumerateArray())
                    .GetProperty("sha256").GetString());
                AssertKnownSixProvenanceLimits(report.RootElement);
            }
            else
            {
                Assert.Contains("subcheck `bug-chain-or-ruling`: **done**", output, StringComparison.Ordinal);
                AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", null);
                Assert.Contains("sha256=" + sha256, output, StringComparison.Ordinal);
                Assert.DoesNotContain("source-report-unreadable", output, StringComparison.Ordinal);
                AssertKnownSixProvenanceLimitsInMarkdown(output);
            }
        }
    }

    [Theory]
    [InlineData("malformed-neighbor", "ruling-invalid-json", ".intent-cli/rulings/intent-cli/intent-cli-dev/R-G864-MALFORMED.json")]
    [InlineData("symlink-target", "ruling-unsafe-path", ".intent-cli/rulings/intent-cli/intent-cli-dev")]
    [InlineData("directory-target", "ruling-unsafe-path", ".intent-cli/rulings/intent-cli/intent-cli-dev")]
    [InlineData("inventory-over-limit", "ruling-inventory-limit", ".intent-cli/rulings/intent-cli/intent-cli-dev")]
    public void ActualStatusPropagatesBoundedRulingReaderFailures(string scenario, string expectedReaderCause, string expectedDiagnosticPath)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-reader-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var digest = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, digest);
        var sourcePath = Path.Combine(host.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-G864-STATUS.json");
        var scopePath = Path.GetDirectoryName(sourcePath)!;
        var originalBytes = File.ReadAllBytes(sourcePath);
        string? outsidePath = null;

        switch (scenario)
        {
            case "malformed-neighbor":
                File.WriteAllText(Path.Combine(scopePath, "R-G864-MALFORMED.json"), "{ truncated", new UTF8Encoding(false));
                break;
            case "symlink-target":
                outsidePath = Path.Combine(physicalTemp, "g864-status-outside-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllBytes(outsidePath, originalBytes);
                File.Delete(sourcePath);
                File.CreateSymbolicLink(sourcePath, outsidePath);
                break;
            case "directory-target":
                File.Delete(sourcePath);
                Directory.CreateDirectory(sourcePath);
                break;
            case "inventory-over-limit":
                for (var index = 0; index < 500; index++)
                    File.WriteAllBytes(Path.Combine(scopePath, $"R-G864-NEIGHBOR-{index:D3}.json"), originalBytes);
                break;
            default:
                throw new InvalidOperationException("Unknown G864 reader-failure case: " + scenario);
        }

        var symlinkRelativePath = ".intent-cli/rulings/intent-cli/intent-cli-dev/R-G864-STATUS.json";
        var before = SnapshotFiles(host.Root, scenario == "symlink-target" ? symlinkRelativePath : null);
        var outsideBefore = outsidePath is null ? null : File.ReadAllBytes(outsidePath);
        foreach (var format in new[] { "json", "markdown" })
        {
            var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
            Assert.Equal(1, exit);
            Assert.Equal(before, SnapshotFiles(host.Root, scenario == "symlink-target" ? symlinkRelativePath : null));
            if (format == "json")
            {
                using var report = JsonDocument.Parse(output);
                var fact = FindBugSource(report.RootElement);
                Assert.Equal("unavailable", fact.GetProperty("state").GetString());
                AssertFactUnavailableClass(fact, "read-failure");
                Assert.Equal("scope-sources-ruling-unavailable", fact.GetProperty("cause").GetString());
                var source = fact.GetProperty("scope_sources");
                Assert.Equal("unavailable", source.GetProperty("state").GetString());
                Assert.Equal("scope-sources-ruling-unavailable", source.GetProperty("cause").GetString());
                var diagnostic = Assert.Single(source.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(expectedReaderCause, diagnostic.GetProperty("cause").GetString());
                Assert.Equal(expectedDiagnosticPath, diagnostic.GetProperty("path").GetString());
                Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
                Assert.Equal(JsonValueKind.Null, source.GetProperty("expected_provenance_block").ValueKind);
                AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
            }
            else
            {
                Assert.Contains("subcheck `bug-chain-or-ruling`: **unavailable**", output, StringComparison.Ordinal);
                AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
                Assert.Contains("cause: `scope-sources-ruling-unavailable`", output, StringComparison.Ordinal);
                Assert.Contains(expectedReaderCause, output, StringComparison.Ordinal);
                Assert.Contains("path=`" + expectedDiagnosticPath + "`", output, StringComparison.Ordinal);
                Assert.DoesNotContain("provenance: reference=", output, StringComparison.Ordinal);
                AssertKnownSixProvenanceLimitsInMarkdown(output);
            }
        }

        if (outsidePath is not null)
        {
            Assert.Equal(outsideBefore, File.ReadAllBytes(outsidePath));
            Assert.True(File.Exists(sourcePath));
            Assert.True(new FileInfo(sourcePath).LinkTarget is not null);
            File.Delete(outsidePath);
        }
        else if (scenario == "malformed-neighbor")
        {
            Assert.Equal(originalBytes, File.ReadAllBytes(sourcePath));
        }
        else if (scenario == "directory-target")
        {
            Assert.True(Directory.Exists(sourcePath));
        }
        else
        {
            Assert.Equal(originalBytes, File.ReadAllBytes(sourcePath));
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void ActualPacketYamlSymlinkFailsClosedWithCapturedDeclaration(string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-packet-symlink-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha256 = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha256);

        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        var packetBytes = File.ReadAllBytes(packetPath);
        var outsidePath = Path.Combine(physicalTemp, "g864-status-packet-sentinel-" + Guid.NewGuid().ToString("N") + ".yaml");
        File.WriteAllBytes(outsidePath, packetBytes);
        File.Delete(packetPath);
        File.CreateSymbolicLink(packetPath, outsidePath);
        Assert.Equal(Encoding.UTF8.GetString(packetBytes), File.ReadAllText(packetPath));
        Assert.Contains("scope_sources:", File.ReadAllText(packetPath), StringComparison.Ordinal);

        const string relativePacketPath = ".intent-cli/issues/G855/packet.yaml";
        var before = SnapshotFiles(host.Root, relativePacketPath);
        var outsideBefore = File.ReadAllBytes(outsidePath);
        var output = RunStatusOutput(host, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(before, SnapshotFiles(host.Root, relativePacketPath));
        Assert.Equal(outsideBefore, File.ReadAllBytes(outsidePath));
        Assert.True(File.Exists(packetPath));
        Assert.Equal(outsidePath, new FileInfo(packetPath).LinkTarget);

        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var fact = FindBugSource(report.RootElement);
            Assert.Equal("unavailable", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, "read-failure");
            Assert.Equal("scope-sources-packet-unavailable", fact.GetProperty("cause").GetString());
            var source = fact.GetProperty("scope_sources");
            Assert.Equal("scope-sources-packet-unavailable", source.GetProperty("cause").GetString());
            Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
            Assert.Equal(JsonValueKind.Null, source.GetProperty("expected_provenance_block").ValueKind);
            Assert.Contains(fact.GetProperty("evidence").EnumerateArray(), evidence =>
                evidence.GetProperty("path").GetString() == relativePacketPath);
            var diagnostic = Assert.Single(source.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("packet-read-failed", diagnostic.GetProperty("cause").GetString());
            Assert.Equal(relativePacketPath, diagnostic.GetProperty("path").GetString());
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.GetProperty("detail").GetString()));
            AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
        }
        else
        {
            Assert.Contains("subcheck `bug-chain-or-ruling`: **unavailable**", output, StringComparison.Ordinal);
            AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "read-failure");
            Assert.Contains("cause: `scope-sources-packet-unavailable`", output, StringComparison.Ordinal);
            Assert.Contains("packet-read-failed", output, StringComparison.Ordinal);
            Assert.Contains("path=`" + relativePacketPath + "`", output, StringComparison.Ordinal);
            Assert.DoesNotContain("provenance: reference=", output, StringComparison.Ordinal);
            Assert.DoesNotContain("expected_provenance_block", output, StringComparison.Ordinal);
            AssertKnownSixProvenanceLimitsInMarkdown(output);
        }
        File.Delete(outsidePath);
    }

    [Fact]
    public void GlobalNonSoloAndIndependentSupportFileFactsDoNotRewriteSourceObservation()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using (var nonSolo = new TempHost(Path.Combine(physicalTemp, "g864-status-nonsolo-" + Guid.NewGuid().ToString("N"))))
        {
            nonSolo.WriteMode("delivery");
            nonSolo.WritePacketAndPublishArtifact();
            var digest = WriteActualG864Ruling(nonSolo);
            WriteG864SourcePacket(nonSolo, digest);
            var before = SnapshotFiles(nonSolo.Root);
            foreach (var format in new[] { "json", "markdown" })
            {
                var reader = new FixedReader();
                var output = RunStatusOutput(nonSolo, reader, format, out var exit);
                Assert.Equal(0, exit);
                Assert.Equal(1, reader.ClaimReads);
                Assert.Equal(0, reader.GitHubReads);
                Assert.Equal(before, SnapshotFiles(nonSolo.Root));
                if (format == "json")
                {
                    using var report = JsonDocument.Parse(output);
                    var fact = FindBugSource(report.RootElement);
                    Assert.Equal("not-applicable", fact.GetProperty("state").GetString());
                    AssertFactUnavailableClass(fact, null);
                    Assert.Equal("recorded-non-solo-mode", fact.GetProperty("cause").GetString());
                    Assert.False(fact.TryGetProperty("scope_sources", out _));
                }
                else
                {
                    Assert.Contains("subcheck `bug-chain-or-ruling`: **not-applicable**", output, StringComparison.Ordinal);
                    AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", null);
                    Assert.Contains("recorded-non-solo-mode", output, StringComparison.Ordinal);
                    Assert.DoesNotContain("scope_sources:", output, StringComparison.Ordinal);
                }
            }
        }

        using (var missingSupport = new TempHost(Path.Combine(physicalTemp, "g864-status-support-" + Guid.NewGuid().ToString("N"))))
        {
            missingSupport.WriteMode(TeamMode.SoloConductor);
            missingSupport.WritePacketAndPublishArtifact();
            var digest = WriteActualG864Ruling(missingSupport);
            WriteG864SourcePacket(missingSupport, digest);
            File.Delete(Path.Combine(missingSupport.Root, ".intent-cli", "issues", "G855", "implementation.md"));
            var before = SnapshotFiles(missingSupport.Root);
            foreach (var format in new[] { "json", "markdown" })
            {
                var output = RunStatusOutput(missingSupport, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
                Assert.Equal(0, exit);
                Assert.Equal(before, SnapshotFiles(missingSupport.Root));
                if (format == "json")
                {
                    using var report = JsonDocument.Parse(output);
                    var fact = FindBugSource(report.RootElement);
                    Assert.Equal("done", fact.GetProperty("state").GetString());
                    AssertFactUnavailableClass(fact, null);
                    Assert.Equal("scope-sources-satisfied", fact.GetProperty("scope_sources").GetProperty("cause").GetString());
                    Assert.Equal("missing", TempHost.FindSubcheck(report.RootElement, "packet-current-files").GetProperty("state").GetString());
                    AssertKnownSixProvenanceLimits(report.RootElement);
                }
                else
                {
                    Assert.Contains("subcheck `bug-chain-or-ruling`: **done**", output, StringComparison.Ordinal);
                    AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", null);
                    Assert.Contains("scope-sources-satisfied", output, StringComparison.Ordinal);
                    Assert.Contains("subcheck `packet-current-files`: **missing**", output, StringComparison.Ordinal);
                    AssertKnownSixProvenanceLimitsInMarkdown(output);
                }
            }
        }
    }

    [Theory]
    [InlineData("domain", "json")]
    [InlineData("domain", "markdown")]
    [InlineData("repository", "json")]
    [InlineData("repository", "markdown")]
    public void ExistingGlobalIdentityConflictsPrecedeOptedInSourceObservation(string identityField, string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-global-identity-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var sha = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha);
        var packetPath = Path.Combine(host.Root, ".intent-cli", "issues", "G855", "packet.yaml");
        var valid = File.ReadAllText(packetPath);
        var mutated = identityField switch
        {
            "domain" => valid.Replace("  domain: intent-cli\n", "  domain: other-domain\n", StringComparison.Ordinal),
            "repository" => valid.Replace("  target_repo: J-Tech-Japan/intent-system\n", "  target_repo: Other-Owner/other-repo\n", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("Unknown global identity field: " + identityField),
        };
        Assert.NotEqual(valid, mutated);
        File.WriteAllText(packetPath, mutated, new UTF8Encoding(false));
        var before = SnapshotFiles(host.Root);
        var reader = CreateKnownSixProvenanceLimitsReader();

        var output = RunStatusOutput(host, reader, format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(before, SnapshotFiles(host.Root));
        Assert.Equal(0, reader.GitHubReads);
        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var root = report.RootElement;
            Assert.Equal("identity-conflict", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
            Assert.DoesNotContain(root.GetProperty("steps").EnumerateArray()
                .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()),
                fact => fact.TryGetProperty("scope_sources", out _));
        }
        else
        {
            Assert.Contains("unavailable_class: `identity-conflict`", output, StringComparison.Ordinal);
            Assert.DoesNotContain("scope_sources:", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    public void UnresolvedGlobalModeIdentityDoesNotSynthesizeScopeSourceProof(string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-unresolved-global-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor, domain: "other-domain", team: "other-team");
        host.WritePacketAndPublishArtifact();
        var sha = WriteActualG864Ruling(host);
        WriteG864SourcePacket(host, sha);
        var before = SnapshotFiles(host.Root);
        var reader = CreateKnownSixProvenanceLimitsReader();

        var output = RunStatusOutput(host, reader, format, out var exit);
        Assert.Equal(1, exit);
        Assert.Equal(before, SnapshotFiles(host.Root));
        Assert.Equal(0, reader.GitHubReads);
        if (format == "json")
        {
            using var report = JsonDocument.Parse(output);
            var root = report.RootElement;
            Assert.Equal("team-mode-unrecorded", root.GetProperty("applicability").GetProperty("cause").GetString());
            Assert.Equal("applicability-unresolved", root.GetProperty("applicability").GetProperty("unavailable_class").GetString());
            Assert.DoesNotContain(root.GetProperty("steps").EnumerateArray()
                .SelectMany(step => step.GetProperty("subchecks").EnumerateArray()),
                fact => fact.TryGetProperty("scope_sources", out _));
        }
        else
        {
            Assert.Contains("team-mode-unrecorded", output, StringComparison.Ordinal);
            Assert.Contains("unavailable_class: `applicability-unresolved`", output, StringComparison.Ordinal);
            Assert.DoesNotContain("scope_sources:", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FreshCloneObservesMissingThenPlainPushedActualRulingWithoutChangingCloneState()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var workspace = new TempHost(Path.Combine(physicalTemp, "g864-status-clones-" + Guid.NewGuid().ToString("N")));
        var bare = Path.Combine(workspace.Root, "origin.git");
        var writerRoot = Path.Combine(workspace.Root, "writer");
        var beforeRoot = Path.Combine(workspace.Root, "before-publication");
        var afterRoot = Path.Combine(workspace.Root, "after-publication");
        RunStatusGit(workspace.Root, "init", "--bare", "--initial-branch=main", bare);
        RunStatusGit(workspace.Root, "clone", bare, writerRoot);
        ConfigureStatusGit(writerRoot);

        // The packet and body are committed first, while the actual pinned ruling
        // remains untracked. A fresh clone therefore sees a valid pin and no artifact.
        var writer = new TempHost(writerRoot);
        writer.WriteMode(TeamMode.SoloConductor);
        writer.WritePacketAndPublishArtifact();
        var digest = WriteActualG864Ruling(writer);
        WriteG864SourcePacket(writer, digest);
        File.Delete(Path.Combine(writerRoot, ".intent-cli", "issues", "G855", "publish.yaml"));
        var modeRelative = Path.GetRelativePath(writerRoot, TeamModeStore.ResolvePath(writerRoot)).Replace(Path.DirectorySeparatorChar, '/');
        RunStatusGit(writerRoot, "add", "--", ".intent-cli/issues/G855", modeRelative);
        RunStatusGit(writerRoot, "commit", "-m", "publish pinned status packet before ruling");
        RunStatusGit(writerRoot, "push", "origin", "HEAD:refs/heads/main");
        RunStatusGit(workspace.Root, "clone", bare, beforeRoot);

        var packetRelative = ".intent-cli/issues/G855/packet.yaml";
        var bodyRelative = ".intent-cli/issues/G855/github-body.md";
        var packetBefore = File.ReadAllBytes(Path.Combine(beforeRoot, packetRelative));
        var bodyBefore = File.ReadAllBytes(Path.Combine(beforeRoot, bodyRelative));
        var missingReader = CreateKnownSixProvenanceLimitsReader();
        var beforeGitState = SnapshotFiles(beforeRoot);
        var beforeGitRefs = SnapshotGitRefs(beforeRoot);
        var missing = RunStatusOutputForRoot(beforeRoot, missingReader, "json", out var missingExit);
        Assert.Equal(0, missingExit);
        Assert.Equal(beforeGitState, SnapshotFiles(beforeRoot));
        Assert.Equal(beforeGitRefs, SnapshotGitRefs(beforeRoot));
        using (var report = JsonDocument.Parse(missing))
        {
            var fact = FindBugSource(report.RootElement);
            Assert.Equal("missing", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, null);
            Assert.Equal("scope-sources-ruling-missing", fact.GetProperty("cause").GetString());
            Assert.Equal(JsonValueKind.Null, fact.GetProperty("scope_sources").GetProperty("provenance").ValueKind);
        }
        var missingPublicState = SnapshotFiles(beforeRoot);
        var missingPublicRefs = SnapshotGitRefs(beforeRoot);
        var missingPublic = RunStatusPublicOutputForRoot(beforeRoot, "json", out var missingPublicExit);
        Assert.Equal(0, missingPublicExit);
        Assert.Equal(missingPublicState, SnapshotFiles(beforeRoot));
        Assert.Equal(missingPublicRefs, SnapshotGitRefs(beforeRoot));
        using (var report = JsonDocument.Parse(missingPublic))
        {
            var fact = FindBugSource(report.RootElement);
            Assert.Equal("missing", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, null);
            Assert.Equal("scope-sources-ruling-missing", fact.GetProperty("cause").GetString());
        }

        var rulingRelative = ".intent-cli/rulings/intent-cli/intent-cli-dev/R-G864-STATUS.json";
        RunStatusGit(writerRoot, "add", "--", rulingRelative);
        RunStatusGit(writerRoot, "commit", "-m", "plain-push actual pinned ruling artifact");
        RunStatusGit(writerRoot, "push", "origin", "HEAD:refs/heads/main");
        RunStatusGit(workspace.Root, "clone", bare, afterRoot);
        Assert.Equal(packetBefore, File.ReadAllBytes(Path.Combine(afterRoot, packetRelative)));
        Assert.Equal(bodyBefore, File.ReadAllBytes(Path.Combine(afterRoot, bodyRelative)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(writerRoot, rulingRelative)), File.ReadAllBytes(Path.Combine(afterRoot, rulingRelative)));

        var completedReader = CreateKnownSixProvenanceLimitsReader();
        var afterGitState = SnapshotFiles(afterRoot);
        var afterGitRefs = SnapshotGitRefs(afterRoot);
        var done = RunStatusOutputForRoot(afterRoot, completedReader, "json", out var doneExit);
        Assert.Equal(0, doneExit);
        Assert.Equal(afterGitState, SnapshotFiles(afterRoot));
        Assert.Equal(afterGitRefs, SnapshotGitRefs(afterRoot));
        using (var report = JsonDocument.Parse(done))
        {
            var root = report.RootElement;
            var fact = FindBugSource(root);
            var source = fact.GetProperty("scope_sources");
            Assert.Equal("done", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, null);
            Assert.Equal("scope-sources-satisfied", source.GetProperty("cause").GetString());
            Assert.Equal("not-verified", source.GetProperty("publication").GetString());
            var provenance = Assert.Single(source.GetProperty("provenance").EnumerateArray());
            Assert.Equal(digest, provenance.GetProperty("sha256").GetString());
            Assert.Equal(rulingRelative, provenance.GetProperty("path").GetString());
        }
        var donePublicState = SnapshotFiles(afterRoot);
        var donePublicRefs = SnapshotGitRefs(afterRoot);
        var donePublic = RunStatusPublicOutputForRoot(afterRoot, "json", out var donePublicExit);
        Assert.Equal(0, donePublicExit);
        Assert.Equal(donePublicState, SnapshotFiles(afterRoot));
        Assert.Equal(donePublicRefs, SnapshotGitRefs(afterRoot));
        using (var report = JsonDocument.Parse(donePublic))
        {
            var fact = FindBugSource(report.RootElement);
            Assert.Equal("done", fact.GetProperty("state").GetString());
            AssertFactUnavailableClass(fact, null);
            var source = fact.GetProperty("scope_sources");
            Assert.Equal("scope-sources-satisfied", source.GetProperty("cause").GetString());
            Assert.Equal(rulingRelative, Assert.Single(source.GetProperty("provenance").EnumerateArray())
                .GetProperty("path").GetString());
        }
        Assert.Equal(1, missingReader.ClaimReads);
        Assert.Equal(0, missingReader.GitHubReads);
        Assert.Equal(1, completedReader.ClaimReads);
        Assert.Equal(0, completedReader.GitHubReads);
    }

    [Fact]
    public void ActualIndependentWriterBranchesExposeCompetingSuccessorsToStatus()
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var workspace = new TempHost(Path.Combine(physicalTemp, "g864-status-forks-" + Guid.NewGuid().ToString("N")));
        var bare = Path.Combine(workspace.Root, "origin.git");
        var seedRoot = Path.Combine(workspace.Root, "seed");
        var leftRoot = Path.Combine(workspace.Root, "left-writer");
        var rightRoot = Path.Combine(workspace.Root, "right-writer");
        var aggregateRoot = Path.Combine(workspace.Root, "aggregate");
        RunStatusGit(workspace.Root, "init", "--bare", "--initial-branch=main", bare);
        RunStatusGit(workspace.Root, "clone", bare, seedRoot);
        ConfigureStatusGit(seedRoot);

        var seed = new TempHost(seedRoot);
        seed.WriteMode(TeamMode.SoloConductor);
        seed.WritePacketAndPublishArtifact();
        var rootRecordedAt = G864RulingTime.AddMinutes(-2);
        var rootDigest = WriteActualG864Ruling(seed, id: "R-G864-FORK-ROOT", recordedAt: rootRecordedAt);
        WriteG864SourcePacket(seed, rootDigest, id: "R-G864-FORK-ROOT");
        RunStatusGit(seedRoot, "add", "--all");
        RunStatusGit(seedRoot, "commit", "-m", "seed pinned root artifact and packet");
        RunStatusGit(seedRoot, "push", "origin", "HEAD:refs/heads/main");

        foreach (var (directory, branch, id) in new[]
        {
            (leftRoot, "left-successor", "R-G864-FORK-LEFT"),
            (rightRoot, "right-successor", "R-G864-FORK-RIGHT"),
        })
        {
            RunStatusGit(workspace.Root, "clone", bare, directory);
            ConfigureStatusGit(directory);
            RunStatusGit(directory, "switch", "--create", branch);
            var branchHost = new TempHost(directory);
            _ = WriteActualG864Ruling(branchHost, id, recordedAt: rootRecordedAt.AddMinutes(1), supersedes: ["R-G864-FORK-ROOT"]);
            var relativeArtifact = $".intent-cli/rulings/intent-cli/intent-cli-dev/{id}.json";
            RunStatusGit(directory, "add", "--", relativeArtifact);
            RunStatusGit(directory, "commit", "-m", "publish independent successor " + branch);
            RunStatusGit(directory, "push", "origin", "HEAD:refs/heads/" + branch);
        }

        RunStatusGit(workspace.Root, "clone", bare, aggregateRoot);
        ConfigureStatusGit(aggregateRoot);
        foreach (var branch in new[] { "left-successor", "right-successor" })
        {
            var remoteBranch = "refs/remotes/origin/" + branch;
            RunStatusGit(aggregateRoot, "fetch", "origin", "refs/heads/" + branch + ":" + remoteBranch);
            RunStatusGit(aggregateRoot, "merge", "--no-edit", remoteBranch);
        }

        var rootArtifactPath = Path.Combine(aggregateRoot, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-G864-FORK-ROOT.json");
        var rootBytes = File.ReadAllBytes(rootArtifactPath);
        Assert.Equal(rootDigest, Convert.ToHexString(SHA256.HashData(rootBytes)).ToLowerInvariant());
        var before = SnapshotFiles(aggregateRoot);
        foreach (var format in new[] { "json", "markdown" })
        {
            var output = RunStatusOutputForRoot(aggregateRoot, CreateKnownSixProvenanceLimitsReader(), format, out var exit);
            Assert.Equal(1, exit);
            Assert.Equal(before, SnapshotFiles(aggregateRoot));
            if (format == "json")
            {
                using var report = JsonDocument.Parse(output);
                var fact = FindBugSource(report.RootElement);
                Assert.Equal("unavailable", fact.GetProperty("state").GetString());
                Assert.Equal("scope-sources-ruling-conflict", fact.GetProperty("cause").GetString());
                AssertFactUnavailableClass(fact, "identity-conflict");
                var source = fact.GetProperty("scope_sources");
                Assert.Equal("scope-sources-ruling-conflict", source.GetProperty("cause").GetString());
                Assert.Contains(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
                    diagnostic.GetProperty("cause").GetString() == "ruling-successor-conflict");
                Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
                AssertKnownSixProvenanceLimits(report.RootElement, expectedObservationExitCode: 1);
            }
            else
            {
                Assert.Contains("cause: `scope-sources-ruling-conflict`", output, StringComparison.Ordinal);
                AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", "identity-conflict");
                Assert.Contains("ruling-successor-conflict", output, StringComparison.Ordinal);
                Assert.DoesNotContain("provenance: reference=", output, StringComparison.Ordinal);
                AssertKnownSixProvenanceLimitsInMarkdown(output);
            }
        }
    }

    private static string NormalizeStatusObservedAt(string json)
    {
        var node = JsonNode.Parse(json)!;
        node["observation"]!["observed_at"] = "<observed-at>";
        return node.ToJsonString();
    }

    private static string NormalizeMarkdownObservedAt(string markdown) => string.Join('\n', markdown
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n')
        .Select(line => line.StartsWith("- observed_at: ", StringComparison.Ordinal) ? "- observed_at: <observed-at>" : line));

    public static IEnumerable<object[]> G864SourceObservationCases()
    {
        var cases = new (string Name, string ExpectedState, string ExpectedCause, string? UnavailableClass)[]
        {
            ("missing", "missing", "scope-sources-ruling-missing", null),
            ("same-id-other-domain", "missing", "scope-sources-ruling-missing", null),
            ("same-id-other-team", "missing", "scope-sources-ruling-missing", null),
            ("wrong-target-repo", "unavailable", "scope-sources-applicability-mismatch", "identity-conflict"),
            ("wrong-execution-unit", "unavailable", "scope-sources-applicability-mismatch", "identity-conflict"),
            ("resolved-team-mismatch", "unavailable", "scope-sources-identity-mismatch", "identity-conflict"),
            ("resolved-team-mismatch-missing", "unavailable", "scope-sources-identity-mismatch", "identity-conflict"),
            ("wrong-digest", "unavailable", "scope-sources-digest-mismatch", "identity-conflict"),
            ("missing-public-block", "missing", "scope-sources-provenance-mismatch", null),
            ("wrong-public-block-digest", "missing", "scope-sources-provenance-mismatch", null),
        };
        foreach (var item in cases)
        foreach (var format in new[] { "json", "markdown" })
            yield return [item.Name, item.ExpectedState, item.ExpectedCause, item.UnavailableClass ?? "", format];
    }

    [Theory]
    [MemberData(nameof(G864SourceObservationCases))]
    public void ActualSourceObservationMapsIdentityDigestAndPublicBlockResults(string scenario, string expectedState,
        string expectedCause, string expectedUnavailableClass, string format)
    {
        var physicalTemp = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        using var host = new TempHost(Path.Combine(physicalTemp, "g864-status-case-" + Guid.NewGuid().ToString("N")));
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();

        var id = "R-G864-STATUS";
        var sourceDomain = "intent-cli";
        var sourceTeam = "intent-cli-dev";
        var sourceRepo = "J-Tech-Japan/intent-system";
        string? writerSha = null;
        switch (scenario)
        {
            case "missing":
                break;
            case "same-id-other-domain":
                writerSha = WriteActualG864Ruling(host, id, domain: "other-domain");
                break;
            case "same-id-other-team":
                writerSha = WriteActualG864Ruling(host, id, team: "other-team");
                break;
            case "wrong-target-repo":
                writerSha = WriteActualG864Ruling(host, id, targetRepo: "Other-Owner/other-repo");
                break;
            case "wrong-execution-unit":
                writerSha = WriteActualG864Ruling(host, id, executionUnits: ["G999"]);
                break;
            case "resolved-team-mismatch":
                sourceTeam = "other-team";
                writerSha = WriteActualG864Ruling(host, id, team: sourceTeam);
                break;
            case "resolved-team-mismatch-missing":
                sourceTeam = "other-team";
                break;
            case "wrong-digest":
                writerSha = WriteActualG864Ruling(host, id);
                break;
            case "missing-public-block":
            case "wrong-public-block-digest":
                writerSha = WriteActualG864Ruling(host, id);
                break;
            default:
                throw new InvalidOperationException("Unrecognized G864 source case: " + scenario);
        }
        var pinnedSha = scenario is "missing" or "resolved-team-mismatch-missing" ? new string('0', 64)
            : scenario == "wrong-digest" ? new string('0', 64)
            : writerSha!;
        var body = scenario switch
        {
            "missing-public-block" => "# G855 request\n\nwrong block\n",
            "wrong-public-block-digest" => "# G855 request\n\n"
                + G864ExpectedPublicBlock(new string('0', 64)) + "\n",
            _ => null,
        };
        WriteG864SourcePacket(host, pinnedSha, id, sourceDomain, sourceTeam, sourceRepo, body: body);

        var before = SnapshotFiles(host.Root);
        var reader = CreateKnownSixProvenanceLimitsReader();
        var output = RunStatusOutput(host, reader, format, out var exit);
        Assert.Equal(expectedState is "missing" ? 0 : 1, exit);
        Assert.Equal(before, SnapshotFiles(host.Root));
        Assert.Equal(1, reader.ClaimReads);
        Assert.Equal(1, reader.GitHubReads);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var fact = FindBugSource(root);
            Assert.Equal(expectedState, fact.GetProperty("state").GetString());
            Assert.Equal(expectedCause, fact.GetProperty("cause").GetString());
            AssertFactUnavailableClass(fact, expectedUnavailableClass.Length == 0 ? null : expectedUnavailableClass);
            var source = fact.GetProperty("scope_sources");
            Assert.Equal(expectedCause, source.GetProperty("cause").GetString());
            AssertKnownSixProvenanceLimits(root, expectedState == "unavailable" ? 1 : 0);
            if (scenario is "missing-public-block" or "wrong-public-block-digest")
            {
                Assert.Equal("missing", fact.GetProperty("state").GetString());
                var provenance = Assert.Single(source.GetProperty("provenance").EnumerateArray());
                Assert.Equal(writerSha, provenance.GetProperty("sha256").GetString());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/" + id + ".json", provenance.GetProperty("path").GetString());
                Assert.Equal(G864ExpectedPublicBlock(writerSha!), source.GetProperty("expected_provenance_block").GetString());
            }
            else if (expectedState == "unavailable")
            {
                Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
                Assert.Equal(JsonValueKind.Null, source.GetProperty("expected_provenance_block").ValueKind);
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
            }
        }
        else
        {
            Assert.Contains("subcheck `bug-chain-or-ruling`: **" + expectedState + "**", output, StringComparison.Ordinal);
            Assert.Contains("cause: `" + expectedCause + "`", output, StringComparison.Ordinal);
            AssertMarkdownFactUnavailableClass(output, "bug-chain-or-ruling", expectedUnavailableClass.Length == 0 ? null : expectedUnavailableClass);
            Assert.Contains("authority_verification: `supplied-not-authenticated`", output, StringComparison.Ordinal);
            Assert.Contains("publication: `not-verified`", output, StringComparison.Ordinal);
            AssertKnownSixProvenanceLimitsInMarkdown(output);
            if (scenario is "missing-public-block" or "wrong-public-block-digest")
            {
                foreach (var line in G864ExpectedPublicBlock(writerSha!).Split('\n'))
                    Assert.Contains("      " + line, output, StringComparison.Ordinal);
                Assert.Contains("path=`.intent-cli/rulings/intent-cli/intent-cli-dev/" + id + ".json`; sha256=`" + writerSha + "`", output, StringComparison.Ordinal);
            }
            else if (expectedState == "unavailable")
            {
                Assert.DoesNotContain("provenance: reference=", output, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void PacketDerivedTraversalDomainIsRejectedBeforeScopedPathResolution()
    {
        using var host = new TempHost();
        host.WritePacketAndPublishArtifact(packetDomain: "../../escape");
        var sentinelPath = Path.Combine(host.Root, "escape", "J-Tech-Japan__intent-system", "queue-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinelPath)!);
        File.WriteAllText(sentinelPath, "not valid queue JSON");
        var before = SnapshotFiles(host.Root);
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        Assert.Equal(before, SnapshotFiles(host.Root));
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("packet-domain-invalid", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ClaimDerivedTraversalTeamIsRejectedBeforeScopedPathResolution()
    {
        using var host = new TempHost();
        host.WritePacketAndPublishArtifact();
        var claim = new UnitStatusClaimRecordFact
        {
            Path = ".intent-cli/claims/active.json",
            Scope = "execution-unit:G855",
            Actor = "builder",
            Team = "../../escape",
            ClaimedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
        };
        var reader = new FixedReader(claims: new UnitStatusClaimSnapshot { State = "completed", ActiveClaim = claim });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("team-scope-invalid", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void MalformedRequiredPublishArtifactIsReadFailureEvenForRecordedDeliveryMode()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        File.WriteAllText(Path.Combine(host.Context.RepoRoot, ".intent-cli", "issues", "G855", "publish.yaml"), "not a publish artifact\n");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("publication-artifact-unreadable", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("read-failure", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Contains(TempHost.FindSubcheck(report.RootElement, "publication-artifact").GetProperty("evidence").EnumerateArray(),
            evidence => evidence.GetProperty("path").GetString() == ".intent-cli/issues/G855/publish.yaml");
    }

    [Fact]
    public void MalformedRequiredPacketIsReadFailureEvenForRecordedDeliveryMode()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        File.WriteAllText(Path.Combine(host.Context.RepoRoot, ".intent-cli", "issues", "G855", "packet.yaml"), "not a packet artifact\n");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("packet-identity-unreadable", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("read-failure", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
        Assert.Contains(TempHost.FindSubcheck(report.RootElement, "packet-current-validation").GetProperty("evidence").EnumerateArray(),
            evidence => evidence.GetProperty("path").GetString() == ".intent-cli/issues/G855/packet.yaml");
    }

    [Fact]
    public void MissingExactModeEntryIsUnavailableAndNeverUsesFallbackOrGithub()
    {
        using var host = new TempHost();
        host.WriteMode("delivery", domain: "other-domain", team: "other-team");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"],
            writer,
            reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("team-mode-unrecorded", document.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", document.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ExplicitDomainWithoutResolvableTeamIsUnavailableAndNeverUsesGithub()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact();
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("team-unresolved", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("applicability-unresolved", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ExplicitTeamConflictingWithRecordedClaimIsIdentityConflict()
    {
        using var host = new TempHost();
        host.WritePacketAndPublishArtifact();
        var claims = new UnitStatusClaimSnapshot
        {
            State = "completed",
            ActiveClaim = new UnitStatusClaimRecordFact
            {
                Path = ClaimCommand.ClaimPath("execution-unit:G855"),
                Scope = "execution-unit:G855",
                Actor = "builder",
                Team = "team-a",
                ClaimedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
            },
        };
        var reader = new FixedReader(claims: claims);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "team-b", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void PacketAndPublishExecutionUnitMismatchIsIdentityConflict()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact(packetUnit: "G854", publishUnit: "G855");
        var reader = new FixedReader();
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        Assert.Equal(0, reader.GitHubReads);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", report.RootElement.GetProperty("applicability").GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void UnreadableCurrentPacketFileIsUnavailableRatherThanReportedMissing()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        Directory.CreateDirectory(Path.Combine(host.Context.RepoRoot, ".intent-cli", "issues", "G855", "implementation.md"));
        using var writer = new StringWriter();
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed",
            HeadBefore = HeadSha,
            HeadAfter = HeadSha,
            HeadSha = HeadSha,
            Facts = [],
        });

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        var fileFact = document.RootElement.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
            .Single(fact => fact.GetProperty("id").GetString() == "packet-current-files");
        Assert.Equal("unavailable", fileFact.GetProperty("state").GetString());
        Assert.Equal("read-failure", fileFact.GetProperty("unavailable_class").GetString());
        Assert.Contains(fileFact.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetProperty("path").GetString() == ".intent-cli/issues/G855/packet.yaml");
    }

    [Theory]
    [InlineData("architect", "design-claim-release")]
    [InlineData("builder", "implementation-claim-release")]
    public void EarlierReleaseCannotSatisfyAReacquiredActiveClaimEpoch(string actor, string releaseFactId)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var claimedAtA = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var claimedAtB = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var oldRelease = TempHost.History("release", actor, actor, claimedAtA,
            DateTimeOffset.Parse("2026-10-02T00:00:00Z"));
        var active = TempHost.Claim(actor, claimedAtB);
        var reader = TempHost.CreateRealAdapter(host, active, [oldRelease]);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var release = TempHost.FindSubcheck(report.RootElement, releaseFactId);
        Assert.Equal("missing", release.GetProperty("state").GetString());
        Assert.Equal("claim-release-superseded-by-new-epoch", release.GetProperty("cause").GetString());
        var oldEvidence = Assert.Single(release.GetProperty("evidence").EnumerateArray());
        Assert.Equal(claimedAtA, oldEvidence.GetProperty("claim_epoch_claimed_at").GetDateTimeOffset());
        Assert.Equal("release", oldEvidence.GetProperty("claim_operation").GetString());
        Assert.Equal(actor == "architect" ? "design-handoff" : "implementation-release",
            oldEvidence.GetProperty("claim_disposition").GetString());
        Assert.Contains("0000-release.json", oldEvidence.GetProperty("path").GetString()!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("architect", "design-claim-acquired", "design-claim-release")]
    [InlineData("builder", "implementation-claim-acquired", "implementation-claim-release")]
    public void ActiveClaimWithoutAnyReleaseHistoryReportsReleaseNotObserved(string actor, string acquiredFactId, string releaseFactId)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var active = TempHost.Claim(actor, DateTimeOffset.Parse("2026-10-03T00:00:00Z"));
        var reader = TempHost.CreateRealAdapter(host, active, []);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, acquiredFactId).GetProperty("state").GetString());
        var release = TempHost.FindSubcheck(report.RootElement, releaseFactId);
        Assert.Equal("missing", release.GetProperty("state").GetString());
        Assert.Equal("claim-release-not-observed", release.GetProperty("cause").GetString());
        Assert.Contains("no release record", release.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        var evidence = Assert.Single(release.GetProperty("evidence").EnumerateArray());
        Assert.Contains("configured-local-claim-snapshot:scope=execution-unit:G855", evidence.GetProperty("provenance").GetString());
        Assert.Contains("ref=refs/remotes/origin/metadata", evidence.GetProperty("provenance").GetString());
        Assert.Contains("oid=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", evidence.GetProperty("provenance").GetString());
    }

    [Fact]
    public void ActualTakeoverTimestampMayMatchItsIncomingActiveClaimEpoch()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var newEpoch = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var takeover = TempHost.History("takeover", "builder", "architect",
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"), newEpoch);
        var active = TempHost.Claim("builder", newEpoch);
        var reader = TempHost.CreateRealAdapter(host, active, [takeover]);
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));

        using var report = JsonDocument.Parse(writer.ToString());
        Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, "implementation-claim-acquired").GetProperty("state").GetString());
        var release = TempHost.FindSubcheck(report.RootElement, "implementation-claim-release");
        Assert.Equal("missing", release.GetProperty("state").GetString());
        Assert.Equal("claim-release-not-observed", release.GetProperty("cause").GetString());
        Assert.Contains("no release record", release.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TakeoverAcquisitionAndLaterMatchingReleaseShareOneClaimEpoch()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var oldEpoch = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var newEpoch = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var history = new[]
        {
            TempHost.History("takeover", "builder", "architect", oldEpoch, newEpoch),
            TempHost.History("release", "builder", "builder", newEpoch, DateTimeOffset.Parse("2026-10-04T00:00:00Z")),
        };
        var reader = TempHost.CreateRealAdapter(host, null, history);
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));

        using var report = JsonDocument.Parse(writer.ToString());
        var release = TempHost.FindSubcheck(report.RootElement, "implementation-claim-release");
        Assert.Equal("done", release.GetProperty("state").GetString());
        Assert.Contains(release.GetProperty("evidence").EnumerateArray(), evidence =>
            evidence.GetProperty("claim_epoch_claimed_at").GetDateTimeOffset() == newEpoch);
    }

    [Theory]
    [InlineData("architect", "design-claim-release")]
    [InlineData("builder", "implementation-claim-release")]
    public void LatestMatchingReleaseSatisfiesOnlyItsOwnClaimEpoch(string actor, string releaseFactId)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        var claimedAtA = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var claimedAtB = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
        var history = new[]
        {
            TempHost.History("release", actor, actor, claimedAtA, DateTimeOffset.Parse("2026-10-02T00:00:00Z")),
            TempHost.History("release", actor, actor, claimedAtB, DateTimeOffset.Parse("2026-10-04T00:00:00Z")),
        };
        var reader = TempHost.CreateRealAdapter(host, null, history);
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var release = TempHost.FindSubcheck(report.RootElement, releaseFactId);
        Assert.Equal("done", release.GetProperty("state").GetString());
        var evidence = Assert.Single(release.GetProperty("evidence").EnumerateArray());
        Assert.Equal(claimedAtB, evidence.GetProperty("claim_epoch_claimed_at").GetDateTimeOffset());
        Assert.Equal("release", evidence.GetProperty("claim_operation").GetString());
        Assert.Contains("0001-release.json", evidence.GetProperty("path").GetString()!, StringComparison.Ordinal);

        using var markdown = new StringWriter();
        var markdownExit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "markdown"], markdown, reader);
        Assert.Equal(0, markdownExit);
        Assert.Contains($"claim_epoch_claimed_at=`{claimedAtB:O}`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("claim_operation=`release`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains(actor == "architect" ? "claim_disposition=`design-handoff`" : "claim_disposition=`implementation-release`",
            markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains($"claim_actor=`{actor}`", markdown.ToString(), StringComparison.Ordinal);
        Assert.Contains("claim_team=`intent-cli-dev`", markdown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSourceIssueUsesOneCanonicalReportTriageAndPlanChain()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
        host.WriteBugSourceChain("BUG-1861", sourceUrl, resolvedPacketRefs: [".intent-cli/issues/G855/packet.yaml"]);
        var childDirectory = Directory.CreateDirectory(Path.Combine(host.Context.RepoRoot, "subdirectory"));
        using var currentDirectory = new CurrentDirectoryScope(childDirectory.FullName);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        Assert.Equal(4, source.GetProperty("evidence").GetArrayLength());
        Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-1861.report.yaml");
        Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-1861.triage.yaml");
        Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-1861.plan.yaml");
    }

    [Theory]
    [InlineData("BUG-20260416-legacy.report.yaml")]
    [InlineData("ZZZ-legacy.report.yaml")]
    public void WriterEmittedTargetChainSkipsOnlyPositivelyForeignLegacySourceReport(string legacyFileName)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var foreignPath = Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs", legacyFileName);
        File.WriteAllText(foreignPath, LegacyForeignSourceReport);
        var foreignBytes = File.ReadAllBytes(foreignPath);
        var beforeStatus = SnapshotFiles(host.Root);

        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        var evidence = source.GetProperty("evidence").EnumerateArray().ToArray();
        Assert.Equal(4, evidence.Length);
        Assert.Equal(new[]
        {
            ".intent-cli/bugs/BUG-G859-TARGET.report.yaml",
            ".intent-cli/bugs/BUG-G859-TARGET.triage.yaml",
            ".intent-cli/bugs/BUG-G859-TARGET.plan.yaml",
            ".intent-cli/issues/G855/packet.yaml",
        }, evidence.Select(item => item.GetProperty("path").GetString()).ToArray());
        Assert.DoesNotContain(evidence, item => item.GetProperty("path").GetString() == ".intent-cli/bugs/" + legacyFileName);
        Assert.Equal(foreignBytes, File.ReadAllBytes(foreignPath));
        Assert.Equal(beforeStatus, SnapshotFiles(host.Root));
        Assert.Equal(1, reader.ClaimReads);
        Assert.Equal(1, reader.GitHubReads);
    }

    [Fact]
    public void ForeignLegacyAdditionPreservesOtherFactSnapshotsAndJsonMarkdownSourceEvidence()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var reader = CreateKnownSixProvenanceLimitsReader();

        using var before = RunStatusJson(host, reader, out var beforeExit);
        Assert.Equal(0, beforeExit);
        AssertKnownSixProvenanceLimits(before.RootElement);
        var legacyPath = Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs", "AAA-legacy.report.yaml");
        File.WriteAllText(legacyPath, LegacyForeignSourceReport);
        var legacyBytes = File.ReadAllBytes(legacyPath);
        using var after = RunStatusJson(host, reader, out var afterExit);
        Assert.Equal(0, afterExit);
        AssertKnownSixProvenanceLimits(after.RootElement);
        var markdown = RunStatusOutput(host, reader, "markdown", out var markdownExit);
        Assert.Equal(0, markdownExit);

        var beforeSource = FindBugSource(before.RootElement);
        var afterSource = FindBugSource(after.RootElement);
        Assert.Equal(beforeSource.GetRawText(), afterSource.GetRawText());
        Assert.Equal("done", afterSource.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", afterSource.GetProperty("cause").GetString());
        Assert.Equal(SnapshotRenderedFacts(before.RootElement, "bug-chain-or-ruling"),
            SnapshotRenderedFacts(after.RootElement, "bug-chain-or-ruling"));
        Assert.Equal(before.RootElement.GetProperty("summary").GetRawText(), after.RootElement.GetProperty("summary").GetRawText());
        Assert.Equal(4, FindBugSource(after.RootElement).GetProperty("evidence").GetArrayLength());
        Assert.Equal(legacyBytes, File.ReadAllBytes(legacyPath));

        var markdownSource = string.Join("\n", markdown.Split('\n')
            .SkipWhile(line => !line.StartsWith("### bug-chain-or-ruling:", StringComparison.Ordinal))
            .TakeWhile((line, index) => index == 0 || !line.StartsWith("### ", StringComparison.Ordinal)));
        Assert.Contains("### bug-chain-or-ruling: done", markdownSource, StringComparison.Ordinal);
        Assert.Contains("- cause: `canonical-source-chain-recorded`", markdownSource, StringComparison.Ordinal);
        foreach (var pointer in afterSource.GetProperty("evidence").EnumerateArray())
        {
            var path = pointer.GetProperty("path").GetString();
            var kind = pointer.GetProperty("kind").GetString();
            Assert.Contains($"kind={kind}; path=`{path}`;", markdownSource, StringComparison.Ordinal);
            Assert.Contains("provenance=" + pointer.GetProperty("provenance").GetString(), markdownSource, StringComparison.Ordinal);
        }
        Assert.Equal(3, reader.ClaimReads);
        Assert.Equal(3, reader.GitHubReads);
    }

    [Fact]
    public void RecordedNonSoloOptionalSourceShortCircuitIsUnchangedByForeignAndMalformedReports()
    {
        using var host = new TempHost();
        host.WriteMode("delivery");
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var reader = new FixedReader();

        using var before = RunStatusJson(host, reader, out var beforeExit);
        Assert.Equal(0, beforeExit);
        var bugs = Path.Combine(host.Root, ".intent-cli", "bugs");
        File.WriteAllText(Path.Combine(bugs, "AAA-foreign.report.yaml"), LegacyForeignSourceReport);
        File.WriteAllText(Path.Combine(bugs, "ZZZ-unscopable.report.yaml"), "bug_id: unrelated\n");
        using var after = RunStatusJson(host, reader, out var afterExit);
        Assert.Equal(0, afterExit);

        Assert.Equal("not-applicable", FindBugSource(before.RootElement).GetProperty("state").GetString());
        Assert.Equal("recorded-non-solo-mode", FindBugSource(after.RootElement).GetProperty("cause").GetString());
        Assert.All(after.RootElement.GetProperty("steps").EnumerateArray(), step =>
        {
            Assert.Equal("not-applicable", step.GetProperty("state").GetString());
            Assert.All(step.GetProperty("subchecks").EnumerateArray(), fact => Assert.Equal("not-applicable", fact.GetProperty("state").GetString()));
        });
        Assert.Equal(SnapshotRenderedFacts(before.RootElement), SnapshotRenderedFacts(after.RootElement));
        Assert.Equal(2, reader.ClaimReads);
        Assert.Equal(0, reader.GitHubReads);
    }

    [Fact]
    public void SourceInventoryBranchesPreserveHostAndGitMetadataSnapshotsAndReaderBudgets()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var gitRefs = Path.Combine(host.Root, ".git", "refs", "heads");
        Directory.CreateDirectory(gitRefs);
        File.WriteAllText(Path.Combine(host.Root, ".git", "index"), "fixture-index\n");
        File.WriteAllText(Path.Combine(gitRefs, "main"), HeadSha + "\n");
        var foreignPath = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-foreign.report.yaml");
        File.WriteAllText(foreignPath, LegacyForeignSourceReport);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        var beforeDone = SnapshotFiles(host.Root);
        using (var done = RunStatusJson(host, reader, out var exit))
        {
            Assert.Equal(0, exit);
            Assert.Equal("done", FindBugSource(done.RootElement).GetProperty("state").GetString());
        }
        Assert.Equal(beforeDone, SnapshotFiles(host.Root));

        var targetPlan = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.plan.yaml");
        var originalTargetPlan = File.ReadAllBytes(targetPlan);
        File.WriteAllText(targetPlan, "not a canonical plan\n");
        var beforeRelevantFailure = SnapshotFiles(host.Root);
        using (var relevantFailure = RunStatusJson(host, reader, out var exit))
        {
            Assert.Equal(1, exit);
            Assert.Equal("unavailable", FindBugSource(relevantFailure.RootElement).GetProperty("state").GetString());
            Assert.Equal("source-triage-plan-unreadable", FindBugSource(relevantFailure.RootElement).GetProperty("cause").GetString());
        }
        Assert.Equal(beforeRelevantFailure, SnapshotFiles(host.Root));

        File.WriteAllBytes(targetPlan, originalTargetPlan);
        var unscopablePath = Path.Combine(host.Root, ".intent-cli", "bugs", "ZZZ-unscopable.report.yaml");
        File.WriteAllText(unscopablePath, "bug_id: unrelated\n");
        var beforeUnscopable = SnapshotFiles(host.Root);
        using (var unscopable = RunStatusJson(host, reader, out var exit))
        {
            Assert.Equal(1, exit);
            var source = FindBugSource(unscopable.RootElement);
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item =>
                item.GetProperty("path").GetString() == ".intent-cli/bugs/ZZZ-unscopable.report.yaml");
        }
        Assert.Equal(beforeUnscopable, SnapshotFiles(host.Root));
        Assert.Equal(3, reader.ClaimReads);
        Assert.Equal(3, reader.GitHubReads);
        Assert.Equal("fixture-index\n", File.ReadAllText(Path.Combine(host.Root, ".git", "index")));
        Assert.Equal(HeadSha + "\n", File.ReadAllText(Path.Combine(gitRefs, "main")));
    }

    [Theory]
    [InlineData("domain_slug")]
    [InlineData("title")]
    public void WriterEmittedCanonicalForeignReportCanBeSkippedOnlyAfterItsRoutingIsProven(string missingField)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string foreignUrl = "https://github.com/elsewhere/intent-system/issues/1878";
        var foreignPath = host.WriteActualBugReport("AAA-G859-FOREIGN", "Foreign report", [foreignUrl], ["G859-OTHER"]);
        var beforeMutation = File.ReadAllText(foreignPath);
        var mutated = string.Join("\n", beforeMutation.Split('\n')
            .Where(line => !line.StartsWith(missingField + ":", StringComparison.Ordinal)));
        Assert.NotEqual(beforeMutation, mutated);
        Assert.Contains(foreignUrl, mutated, StringComparison.Ordinal);
        Assert.Contains("G859-OTHER", mutated, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => BugReportArtifactYaml.Deserialize(mutated));
        File.WriteAllText(foreignPath, mutated);
        var mutatedBytes = File.ReadAllBytes(foreignPath);

        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(0, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        Assert.DoesNotContain(source.GetProperty("evidence").EnumerateArray(), item => item.GetProperty("path").GetString() == Path.GetRelativePath(host.Root, foreignPath).Replace('\\', '/'));
        Assert.Equal(mutatedBytes, File.ReadAllBytes(foreignPath));
    }

    [Fact]
    public void TargetClaimsInEitherRoutingFormDominateForeignClaims()
    {
        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var targetPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.report.yaml");
            File.AppendAllText(targetPath,
                "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  execution_unit: OTHER-UNIT\n");
            Assert.NotNull(BugReportArtifactYaml.Deserialize(File.ReadAllText(targetPath)));
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling").GetProperty("state").GetString());
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            const string sameNumberForeignUrl = "https://github.com/elsewhere/intent-system/issues/1878";
            var foreignPath = host.WriteActualBugReport("AAA-G859-FOREIGN", "Foreign same-number report", [sameNumberForeignUrl], ["G855"]);
            File.AppendAllText(foreignPath, $"observed_in:\n  linked_issue: \"{SourceIssueUrl}\"\n  execution_unit: G855\n");
            Assert.NotNull(BugReportArtifactYaml.Deserialize(File.ReadAllText(foreignPath)));
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(1, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-chain-identity-conflict", source.GetProperty("cause").GetString());
            Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void LegacyTargetUrlVetoesCanonicalForeignIssueAndUnitWhenReportSchemaIsInvalid()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string canonicalForeignUrl = "https://github.com/elsewhere/intent-system/issues/1878";
        const string reportFileName = "AAA-G859-LEGACY-TARGET.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-LEGACY-TARGET", "Foreign canonical report", [canonicalForeignUrl], ["OTHER-UNIT"]);
        var emitted = File.ReadAllText(reportPath);
        var canonical = BugReportArtifactYaml.Deserialize(emitted);
        Assert.Equal([canonicalForeignUrl], canonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], canonical.LinkedExecutionUnits);

        var dualRouted = emitted + "observed_in:\n  linked_issue: \"" + SourceIssueUrl
            + "\"\n  execution_unit: OTHER-LEGACY-UNIT\n";
        Assert.Contains("linked_issue: \"" + SourceIssueUrl + "\"", dualRouted, StringComparison.Ordinal);
        Assert.Contains("execution_unit: OTHER-LEGACY-UNIT", dualRouted, StringComparison.Ordinal);
        var dualCanonical = BugReportArtifactYaml.Deserialize(dualRouted);
        Assert.Equal([canonicalForeignUrl], dualCanonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], dualCanonical.LinkedExecutionUnits);
        var invalid = RemoveTopLevelYamlField(dualRouted, "title");
        Assert.NotEqual(dualRouted, invalid);

        AssertMalformedSourceInventoryReport(host, reportFileName, invalid, SourceIssueUrl);
    }

    [Fact]
    public void CanonicalTargetUrlVetoesLegacyForeignIssueAndUnitWhenReportSchemaIsInvalid()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string reportFileName = "AAA-G859-CANONICAL-TARGET.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-CANONICAL-TARGET", "Target canonical report", [SourceIssueUrl], ["OTHER-UNIT"]);
        var emitted = File.ReadAllText(reportPath);
        var canonical = BugReportArtifactYaml.Deserialize(emitted);
        Assert.Equal([SourceIssueUrl], canonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], canonical.LinkedExecutionUnits);

        var dualRouted = emitted
            + "observed_in:\n  linked_issue: \"" + ForeignIssueUrl
            + "\"\n  execution_unit: OTHER-LEGACY-UNIT\n";
        Assert.Contains("linked_issue: \"" + ForeignIssueUrl + "\"", dualRouted, StringComparison.Ordinal);
        Assert.Contains("execution_unit: OTHER-LEGACY-UNIT", dualRouted, StringComparison.Ordinal);
        var dualCanonical = BugReportArtifactYaml.Deserialize(dualRouted);
        Assert.Equal([SourceIssueUrl], dualCanonical.LinkedIssueRefs);
        Assert.Equal(["OTHER-UNIT"], dualCanonical.LinkedExecutionUnits);
        var invalid = RemoveTopLevelYamlField(dualRouted, "title");
        Assert.NotEqual(dualRouted, invalid);

        AssertMalformedSourceInventoryReport(host, reportFileName, invalid, SourceIssueUrl);
    }

    [Fact]
    public void LegacyCurrentUnitAndForeignIssueCannotBeExcluded()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var currentUnitLegacy = LegacyForeignSourceReport.Replace("TOY-CALC-V0-01", "G855", StringComparison.Ordinal);
        var legacyPath = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-current-unit.report.yaml");
        File.WriteAllText(legacyPath, currentUnitLegacy);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        Assert.Contains("AAA-current-unit.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MatchedTargetSchemaAndChainFailuresRemainStrict()
    {
        var cases = new[]
        {
            (Name: "missing-title", ExpectedState: "unavailable", ExpectedCause: "source-report-unreadable"),
            (Name: "wrong-embedded-id", ExpectedState: "unavailable", ExpectedCause: "source-report-identity-conflict"),
            (Name: "malformed-triage", ExpectedState: "unavailable", ExpectedCause: "source-triage-plan-unreadable"),
            (Name: "malformed-plan", ExpectedState: "unavailable", ExpectedCause: "source-triage-plan-unreadable"),
            (Name: "missing-triage", ExpectedState: "missing", ExpectedCause: "source-triage-plan-not-recorded"),
            (Name: "missing-plan", ExpectedState: "missing", ExpectedCause: "source-triage-plan-not-recorded"),
        };

        foreach (var item in cases)
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var reportPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.report.yaml");
            var triagePath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.triage.yaml");
            var planPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-G859-TARGET.plan.yaml");
            switch (item.Name)
            {
                case "missing-title":
                    File.WriteAllText(reportPath, string.Join("\n", File.ReadAllText(reportPath).Split('\n')
                        .Where(line => !line.StartsWith("title:", StringComparison.Ordinal))));
                    break;
                case "wrong-embedded-id":
                    File.WriteAllText(reportPath, File.ReadAllText(reportPath).Replace("bug_id: BUG-G859-TARGET", "bug_id: BUG-G859-OTHER", StringComparison.Ordinal));
                    break;
                case "malformed-triage":
                    File.WriteAllText(triagePath, "not a canonical triage artifact\n");
                    break;
                case "malformed-plan":
                    File.WriteAllText(planPath, "not a canonical plan artifact\n");
                    break;
                case "missing-triage":
                    File.Delete(triagePath);
                    break;
                case "missing-plan":
                    File.Delete(planPath);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected strict-chain fixture.");
            }

            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var report = RunStatusJson(host, reader, out var exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal(item.ExpectedState, source.GetProperty("state").GetString());
            Assert.Equal(item.ExpectedCause, source.GetProperty("cause").GetString());
            Assert.Equal(item.ExpectedState == "missing" ? 0 : 1, exit);
            if (item.Name == "missing-title")
                Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
            if (item.Name == "wrong-embedded-id")
                Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void DirectReportPathKeepsStrictValidationAndDoesNotUseForeignProjection()
    {
        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/BUG-G859-TARGET.report.yaml");
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("done", source.GetProperty("state").GetString());
            Assert.Contains(source.GetProperty("evidence").EnumerateArray(), item =>
                item.GetProperty("path").GetString() == ".intent-cli/bugs/BUG-G859-TARGET.report.yaml"
                && item.GetProperty("url").ValueKind == JsonValueKind.Null);
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/BUG-20260416-fix-worktree-diff-blocked-after-backend-exit.report.yaml");
            var foreignPath = Path.Combine(host.Root, ".intent-cli", "bugs", "BUG-20260416-fix-worktree-diff-blocked-after-backend-exit.report.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!);
            File.WriteAllText(foreignPath, LegacyForeignSourceReport);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(1, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Contains(".intent-cli/bugs/BUG-20260416-fix-worktree-diff-blocked-after-backend-exit.report.yaml",
                source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/nested/BUG-G859-TARGET.report.yaml");
            Directory.CreateDirectory(Path.Combine(host.Root, ".intent-cli", "bugs", "nested"));
            File.WriteAllText(Path.Combine(host.Root, ".intent-cli", "bugs", "nested", "BUG-G859-TARGET.report.yaml"), LegacyForeignSourceReport);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(1, exit);
            var source = FindBugSource(report.RootElement);
            Assert.Equal("source-reference-identity-conflict", source.GetProperty("cause").GetString());
            Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void MalformedOrAmbiguousRoutingNeverAuthorizesForeignExclusion()
    {
        var uncertainReports = new[]
        {
            "linked_issue_refs: null\n",
            "linked_issue_refs:\n  - null\n",
            "linked_issue_refs:\n  - 1878\n",
            "linked_issue_refs:\n  - {issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"}\n",
            "linked_execution_units: null\n",
            "linked_execution_units:\n  - null\n",
            "linked_execution_units:\n  - [G855]\n",
            "linked_execution_units:\n  - ../unsafe\n",
            "observed_in: null\n",
            "observed_in:\n  linked_issue: [\"https://github.com/tomohisa/toy-calc-sample/issues/3\"]\n  execution_unit: G855\n",
            "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n",
            "observed_in:\n  execution_unit: TOY-CALC-V0-01\n",
            "linked_issue_refs:\n  - \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\nlinked_issue_refs:\n  - \"https://github.com/elsewhere/project/issues/2\"\n",
            "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  linked_issue: \"https://github.com/elsewhere/project/issues/2\"\n  execution_unit: TOY-CALC-V0-01\n",
            "metadata:\n  linked_issue_refs:\n    - \"https://github.com/J-Tech-Japan/intent-system/issues/1878\"\n  linked_execution_units: [G855]\n",
            "broken: [\n",
            "just a scalar\n",
            "linked_issue_refs:\n  - \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\nlinked_execution_units:\n  - TOY-CALC-V0-01\n---\nlinked_issue_refs:\n  - \"https://github.com/J-Tech-Japan/intent-system/issues/1878\"\n",
        };

        foreach (var yaml in uncertainReports)
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
            var path = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-UNCERTAIN.report.yaml");
            File.WriteAllText(path, yaml);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);

            Assert.Equal(1, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
            Assert.Contains("AAA-UNCERTAIN.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
            var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
            Assert.Equal(".intent-cli/bugs/AAA-UNCERTAIN.report.yaml", attempted.GetProperty("path").GetString());
            Assert.Equal(SourceIssueUrl, attempted.GetProperty("url").GetString());
            Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
        }
    }

    public static IEnumerable<object[]> MalformedRoutingWithIndependentForeignProofCases()
    {
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units: null\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units: OTHER-UNIT\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units:\n  - null\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units:\n  - [OTHER-UNIT]\n" };
        yield return new object[] { "canonical", "linked_execution_units", "linked_execution_units:\n  - ../unsafe\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in: null\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in: OTHER\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in: []\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  linked_issue: \"" + ForeignIssueUrl + "\"\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  execution_unit: OTHER-UNIT\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  linked_issue: [\"" + ForeignIssueUrl + "\"]\n  execution_unit: OTHER-UNIT\n" };
        yield return new object[] { "canonical", "observed_in", "observed_in:\n  linked_issue: \"" + ForeignIssueUrl + "\"\n  execution_unit: {unit: OTHER-UNIT}\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs: null\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs: \"" + ForeignIssueUrl + "\"\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - null\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - 1878\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - {issue: \"" + ForeignIssueUrl + "\"}\n" };
        yield return new object[] { "legacy", "linked_issue_refs", "linked_issue_refs:\n  - \"" + ForeignIssueUrl + "\"\n  - 1878\n" };
    }

    [Theory]
    [MemberData(nameof(MalformedRoutingWithIndependentForeignProofCases))]
    public void MalformedRoutingVetoesExclusionEvenWhenOtherFieldsProveForeignIdentity(
        string sourceProof, string malformedField, string malformedRouting)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string reportFileName = "AAA-G859-VETO.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-VETO", "Foreign veto fixture",
            sourceProof == "canonical" ? [ForeignIssueUrl] : [],
            sourceProof == "canonical" ? ["OTHER-UNIT"] : []);
        var yaml = File.ReadAllText(reportPath);

        if (sourceProof == "canonical")
        {
            Assert.Contains("linked_issue_refs:\n  - \"" + ForeignIssueUrl + "\"", yaml, StringComparison.Ordinal);
        }
        else
        {
            var legacyProof = "observed_in:\n  linked_issue: \"" + ForeignIssueUrl
                + "\"\n  execution_unit: OTHER-UNIT\n";
            yaml = SetTopLevelYamlField(yaml, "observed_in", legacyProof);
            Assert.Contains(legacyProof.TrimEnd('\n'), yaml, StringComparison.Ordinal);
        }

        yaml = SetTopLevelYamlField(yaml, malformedField, malformedRouting);
        Assert.Contains(malformedRouting.TrimEnd('\n'), yaml, StringComparison.Ordinal);
        var invalid = RemoveTopLevelYamlField(yaml, "title");
        Assert.NotEqual(yaml, invalid);

        AssertMalformedSourceInventoryReport(host, reportFileName, invalid, SourceIssueUrl);
    }

    [Fact]
    public void NestedMetadataTargetLookalikesDoNotVetoCanonicalForeignExclusion()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        const string reportFileName = "AAA-G859-METADATA.report.yaml";
        var reportPath = host.WriteActualBugReport("AAA-G859-METADATA", "Foreign report with metadata lookalikes",
            [ForeignIssueUrl], ["OTHER-UNIT"]);
        var emitted = File.ReadAllText(reportPath);
        var withMetadata = emitted
            + "metadata:\n  linked_issue_refs:\n    - \"" + SourceIssueUrl
            + "\"\n  linked_execution_units: [G855]\n";
        Assert.Contains("linked_issue_refs:\n  - \"" + ForeignIssueUrl + "\"", withMetadata, StringComparison.Ordinal);
        Assert.Contains("    - \"" + SourceIssueUrl + "\"", withMetadata, StringComparison.Ordinal);
        Assert.Contains("linked_execution_units: [G855]", withMetadata, StringComparison.Ordinal);
        var invalid = RemoveTopLevelYamlField(withMetadata, "title");
        Assert.NotEqual(withMetadata, invalid);
        Assert.Throws<InvalidOperationException>(() => BugReportArtifactYaml.Deserialize(invalid));
        File.WriteAllText(reportPath, invalid);
        var bytes = File.ReadAllBytes(reportPath);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(0, exit);
        var source = FindBugSource(report.RootElement);
        Assert.Equal("done", source.GetProperty("state").GetString());
        Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        Assert.DoesNotContain(source.GetProperty("evidence").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == ".intent-cli/bugs/" + reportFileName);
        Assert.Equal(bytes, File.ReadAllBytes(reportPath));
    }

    [Fact]
    public void YamlDotNetKeepsImplicitNullAndExplicitStringUnitTagsDistinct()
    {
        static YamlScalarNode ReadExecutionUnit(string value)
        {
            var stream = new YamlStream();
            using var reader = new StringReader("observed_in:\n  execution_unit: " + value + "\n");
            stream.Load(reader);
            var root = Assert.IsType<YamlMappingNode>(stream.Documents.Single().RootNode);
            var observedIn = Assert.IsType<YamlMappingNode>(root.Children[new YamlScalarNode("observed_in")]);
            return Assert.IsType<YamlScalarNode>(observedIn.Children[new YamlScalarNode("execution_unit")]);
        }

        var implicitNull = ReadExecutionUnit("null");
        var explicitString = ReadExecutionUnit("!!str null");
        var quotedString = ReadExecutionUnit("\"null\"");
        var explicitlyNull = ReadExecutionUnit("!!null G859-OTHER");

        Assert.Equal("null", explicitString.Value);
        Assert.Equal("null", quotedString.Value);
        Assert.NotEqual(implicitNull.Tag, explicitString.Tag);
        Assert.Equal(YamlDotNet.Core.ScalarStyle.Plain, explicitString.Style);
        Assert.NotEqual(implicitNull.Tag, explicitlyNull.Tag);
    }

    [Theory]
    [InlineData("null", "unavailable")]
    [InlineData("~", "unavailable")]
    [InlineData("!!null G859-OTHER", "unavailable")]
    [InlineData("!!str null", "done")]
    [InlineData("\"null\"", "done")]
    public void LegacyUnitScalarNullnessDoesNotConfuseExplicitStringUnits(string unitYaml, string expectedState)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var path = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-LEGACY-UNIT.report.yaml");
        File.WriteAllText(path,
            "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  execution_unit: " + unitYaml + "\n");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(expectedState == "done" ? 0 : 1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal(expectedState, source.GetProperty("state").GetString());
        if (expectedState == "unavailable")
        {
            Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        }
        else
        {
            Assert.Equal("canonical-source-chain-recorded", source.GetProperty("cause").GetString());
        }
    }

    [Fact]
    public void DisappearedInventoryMemberRetainsExactReadFailureAlongsideTarget()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var missingTarget = Path.Combine(host.Root, "removed-before-read.report.yaml");
        var disappearedPath = Path.Combine(host.Root, ".intent-cli", "bugs", "AAA-DISAPPEARED.report.yaml");
        File.CreateSymbolicLink(disappearedPath, missingTarget);
        Assert.Contains(disappearedPath, Directory.EnumerateFiles(Path.GetDirectoryName(disappearedPath)!, "*.report.yaml"));
        Assert.ThrowsAny<IOException>(() => File.ReadAllText(disappearedPath));
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Contains("AAA-DISAPPEARED.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
        Assert.Equal(".intent-cli/bugs/AAA-DISAPPEARED.report.yaml", attempted.GetProperty("path").GetString());
        Assert.Equal(SourceIssueUrl, attempted.GetProperty("url").GetString());
        Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
    }

    [Fact]
    public void TargetMatchDoesNotHideLaterUnscopableInventoryMember()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var laterPath = Path.Combine(host.Root, ".intent-cli", "bugs", "ZZZ-target-unscopable.report.yaml");
        File.WriteAllText(laterPath,
            "bug_id: ZZZ-target-unscopable\nlinked_issue_refs:\n  - \"" + SourceIssueUrl + "\"\n");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Contains("ZZZ-target-unscopable.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAndCompleteCanonicalNonmatchInventoriesRemainMissing()
    {
        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("missing", source.GetProperty("state").GetString());
            Assert.Equal("source-chain-not-recorded", source.GetProperty("cause").GetString());
        }

        using (var host = new TempHost())
        {
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
            host.WriteActualBugReport("BUG-G859-NONMATCH", "Canonical report without source links", [], []);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });

            using var report = RunStatusJson(host, reader, out var exit);
            Assert.Equal(0, exit);
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("missing", source.GetProperty("state").GetString());
            Assert.Equal("source-chain-not-recorded", source.GetProperty("cause").GetString());
        }
    }

    [Theory]
    [InlineData(499, "done", "canonical-source-chain-recorded")]
    [InlineData(500, "unavailable", "source-inventory-truncated")]
    public void SourceInventoryBoundStillCountsEveryUrlReport(int foreignReportCount, string expectedState, string expectedCause)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(sourceArtifact: SourceIssueUrl);
        host.WriteActualBugSourceChain("BUG-G859-TARGET", SourceIssueUrl);
        var bugs = Path.Combine(host.Root, ".intent-cli", "bugs");
        Directory.CreateDirectory(bugs);
        const string unrelated = "observed_in:\n  linked_issue: \"https://github.com/tomohisa/toy-calc-sample/issues/3\"\n  execution_unit: OTHER-UNIT\n";
        for (var index = 0; index < foreignReportCount; index++)
            File.WriteAllText(Path.Combine(bugs, $"AAA-G859-FOREIGN-{index:D3}.report.yaml"), unrelated);
        Assert.Equal(foreignReportCount + 1, Directory.EnumerateFiles(bugs, "*.report.yaml").Count());
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });

        using var report = RunStatusJson(host, reader, out var exit);

        Assert.Equal(expectedState == "done" ? 0 : 1, exit);
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal(expectedState, source.GetProperty("state").GetString());
        Assert.Equal(expectedCause, source.GetProperty("cause").GetString());
        if (expectedState == "unavailable")
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void UnrelatedMalformedReportMakesSourceInventoryUnavailableWithPathAndProvenance()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
        host.WriteBugSourceChain("BUG-1861", sourceUrl);
        var malformedPath = Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs", "UNRELATED.report.yaml");
        File.WriteAllText(malformedPath, "bug_id: unrelated\n");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-report-unreadable", source.GetProperty("cause").GetString());
        Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        Assert.Contains(sourceUrl, source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains(".intent-cli/bugs/UNRELATED.report.yaml", source.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var attempted = Assert.Single(source.GetProperty("evidence").EnumerateArray());
        Assert.Equal("bug-report-artifact", attempted.GetProperty("kind").GetString());
        Assert.Equal(".intent-cli/bugs/UNRELATED.report.yaml", attempted.GetProperty("path").GetString());
        Assert.Equal(sourceUrl, attempted.GetProperty("url").GetString());
        Assert.Equal("attempted-canonical-source-inventory-read", attempted.GetProperty("provenance").GetString());
        Assert.Equal("done", TempHost.FindSubcheck(report.RootElement, "publication-artifact").GetProperty("state").GetString());
    }

    [Fact]
    public void ExplicitUnsupportedRulingReferenceRemainsProvenanceLimited()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(rulingArtifact: ".intent-cli/rulings/G855.yaml");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("ruling-reference-unsupported", source.GetProperty("cause").GetString());
        Assert.Equal("provenance-limit", source.GetProperty("unavailable_class").GetString());
        Assert.Empty(source.GetProperty("repair_commands").EnumerateArray());
        Assert.Equal("no-supported-ruling-artifact-reader", source.GetProperty("repair_unavailable_reason").GetString());
    }

    [Theory]
    [InlineData("foreign-repo", "G855")]
    [InlineData("same-repo", "G854")]
    public void ForeignOrContradictoryCanonicalSourceIdentityIsUnavailable(string linkKind, string linkedUnit)
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        var reportLink = linkKind == "foreign-repo"
            ? "https://github.com/elsewhere/intent-system/issues/1861"
            : sourceUrl;
        host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
        host.WriteBugSourceChain("BUG-1861", sourceUrl, linkedIssueUrl: reportLink, linkedUnits: [linkedUnit]);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
        Assert.Equal("unavailable", source.GetProperty("state").GetString());
        Assert.Equal("source-chain-identity-conflict", source.GetProperty("cause").GetString());
        Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
    }

    [Fact]
    public void ExplicitLocalSourcePathThatCannotBeReadIsUnavailableReadFailure()
    {
        foreach (var malformed in new[] { false, true })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: ".intent-cli/bugs/BUG-1861.report.yaml");
            if (malformed)
            {
                var bugDirectory = Directory.CreateDirectory(Path.Combine(host.Context.RepoRoot, ".intent-cli", "bugs"));
                File.WriteAllText(Path.Combine(bugDirectory.FullName, "BUG-1861.report.yaml"), "not a canonical bug report\n");
            }
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(1, exit);
            using var report = JsonDocument.Parse(writer.ToString());
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("read-failure", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void AmbiguousOrMismatchedCanonicalSourceChainIsUnavailable()
    {
        const string sourceUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1861";
        foreach (var duplicate in new[] { true, false })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(sourceArtifact: sourceUrl);
            host.WriteBugSourceChain("BUG-1861", sourceUrl, duplicate: duplicate, corruptPlan: !duplicate);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(1, exit);
            using var report = JsonDocument.Parse(writer.ToString());
            var source = TempHost.FindSubcheck(report.RootElement, "bug-chain-or-ruling");
            Assert.Equal("unavailable", source.GetProperty("state").GetString());
            Assert.Equal("identity-conflict", source.GetProperty("unavailable_class").GetString());
        }
    }

    [Fact]
    public void LegacyWritebackAbsenceIsMissingAndExplicitFalseIsNotApplicable()
    {
        foreach (var explicitFalse in new[] { false, true })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(knowledgeRequired: explicitFalse ? false : null);
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            var exit = UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

            Assert.Equal(0, exit);
            using var report = JsonDocument.Parse(writer.ToString());
            var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
            var orchestrator = TempHost.FindSubcheck(report.RootElement, "orchestrator-knowledge-writeback");
            Assert.Equal(explicitFalse ? "not-applicable" : "missing", architect.GetProperty("state").GetString());
            Assert.Equal(explicitFalse ? "knowledge-writeback-not-required" : "knowledge-writeback-declaration-absent", architect.GetProperty("cause").GetString());
            Assert.Equal(explicitFalse ? "not-applicable" : "missing", orchestrator.GetProperty("state").GetString());
            Assert.Equal("missing", TempHost.FindSubcheck(report.RootElement, "guide-reachability").GetProperty("state").GetString());
        }
    }

    [Fact]
    public void LegacyUnattributedWritebackIsRetainedButDoesNotClearArchitectDuty()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(knowledgeRequired: true);
        host.WriteWritebackRecord(role: null, "record.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(0, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
        Assert.Equal("unavailable", architect.GetProperty("state").GetString());
        Assert.Equal("provenance-limit", architect.GetProperty("unavailable_class").GetString());
        Assert.Contains(architect.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetProperty("role").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void RoleAliasesAreNormalizedAndOrchestratorRecordDoesNotSatisfyArchitectDuty()
    {
        foreach (var role in new[] { "design", "orchestrator" })
        {
            using var host = new TempHost();
            host.WriteMode(TeamMode.SoloConductor);
            host.WritePacketAndPublishArtifact(knowledgeRequired: true);
            host.WriteWritebackRecord(role, role + ".json");
            var reader = new FixedReader(new UnitStatusRemoteSnapshot
            {
                State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
            });
            using var writer = new StringWriter();

            Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
                ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
            using var report = JsonDocument.Parse(writer.ToString());
            var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
            Assert.Equal(role == "design" ? "done" : "missing", architect.GetProperty("state").GetString());
            var architectEvidence = Assert.Single(architect.GetProperty("evidence").EnumerateArray());
            Assert.Equal(role == "design" ? "architect" : role, architectEvidence.GetProperty("role").GetString());
            Assert.Contains("required-role=architect; qualifies=" + (role == "design" ? "true" : "false"),
                architectEvidence.GetProperty("provenance").GetString(), StringComparison.Ordinal);
            var orchestrator = TempHost.FindSubcheck(report.RootElement, "orchestrator-knowledge-writeback");
            Assert.Equal(role == "orchestrator" ? "done" : "missing",
                orchestrator.GetProperty("state").GetString());
            Assert.Single(orchestrator.GetProperty("evidence").EnumerateArray());
        }
    }

    [Fact]
    public void GuideRecordsPreserveAllRoleEvidenceWhileOnlyArchitectQualifies()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(guideDeclared: true);
        host.WriteGuideRecord("architect", ["architect"], roleSlot: true, fileName: "architect.json");
        host.WriteGuideRecord("orchestrator", ["architect"], roleSlot: true, fileName: "orchestrator.json");
        Assert.Equal(2, RoleScopedCloseoutRecordStore.EnumerateExistingPaths(
            host.Context.RepoRoot, GuideReachabilityRecord.RecordRootRelativePath, "G855").Count);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        var guide = TempHost.FindSubcheck(report.RootElement, "guide-reachability");
        Assert.Equal("guide-reachability-recorded", guide.GetProperty("cause").GetString());
        Assert.Equal("done", guide.GetProperty("state").GetString());
        var evidence = guide.GetProperty("evidence").EnumerateArray().ToArray();
        Assert.Equal(2, evidence.Length);
        Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() == "architect"
            && pointer.GetProperty("provenance").GetString()!.Contains("required-role=architect; qualifies=true", StringComparison.Ordinal));
        Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() == "orchestrator"
            && pointer.GetProperty("provenance").GetString()!.Contains("required-role=architect; qualifies=false", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateGuideArchitectRecordsRetainEveryObservedRolePointer()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(guideDeclared: true);
        host.WriteGuideRecord("architect", ["architect"], roleSlot: true, fileName: "architect-a.json");
        host.WriteGuideRecord("design", ["architect"], roleSlot: true, fileName: "architect-b.json");
        host.WriteGuideRecord("orchestrator", ["architect"], roleSlot: true, fileName: "orchestrator.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(1, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        var guide = TempHost.FindSubcheck(report.RootElement, "guide-reachability");
        Assert.Equal("unavailable", guide.GetProperty("state").GetString());
        Assert.Equal("identity-conflict", guide.GetProperty("unavailable_class").GetString());
        Assert.Equal(3, guide.GetProperty("evidence").GetArrayLength());
    }

    [Fact]
    public void DuplicateArchitectWritebackRecordsAreUnavailableAndNotSelectedByOrder()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(knowledgeRequired: true);
        host.WriteWritebackRecord("architect", "architect.json");
        host.WriteWritebackRecord("design", "design.json");
        host.WriteWritebackRecord("orchestrator", "orchestrator.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        var exit = UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader);

        Assert.Equal(1, exit);
        using var report = JsonDocument.Parse(writer.ToString());
        var architect = TempHost.FindSubcheck(report.RootElement, "architect-knowledge-writeback");
        Assert.Equal("unavailable", architect.GetProperty("state").GetString());
        Assert.Equal("identity-conflict", architect.GetProperty("unavailable_class").GetString());
        Assert.Equal(3, architect.GetProperty("evidence").GetArrayLength());
        Assert.Contains(architect.GetProperty("evidence").EnumerateArray(), pointer =>
            pointer.GetProperty("role").GetString() == "orchestrator"
            && pointer.GetProperty("provenance").GetString()!.Contains("required-role=architect; qualifies=false", StringComparison.Ordinal));
    }

    [Fact]
    public void WritebackFactsRetainBothRolePointersAndQualifyEachDutySeparately()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(knowledgeRequired: true);
        host.WriteWritebackRecord("architect", "architect.json");
        host.WriteWritebackRecord("orchestrator", "orchestrator.json");
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        foreach (var (id, expectedRole) in new[]
        {
            ("architect-knowledge-writeback", "architect"),
            ("orchestrator-knowledge-writeback", "orchestrator"),
        })
        {
            var fact = TempHost.FindSubcheck(report.RootElement, id);
            Assert.Equal("done", fact.GetProperty("state").GetString());
            var evidence = fact.GetProperty("evidence").EnumerateArray().ToArray();
            Assert.Equal(2, evidence.Length);
            Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() == expectedRole
                && pointer.GetProperty("provenance").GetString()!.Contains("qualifies=true", StringComparison.Ordinal));
            Assert.Contains(evidence, pointer => pointer.GetProperty("role").GetString() != expectedRole
                && pointer.GetProperty("provenance").GetString()!.Contains("qualifies=false", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void GuideAudienceRoleDoesNotReplaceGuideRecorderRole()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact(guideDeclared: true);
        host.WriteGuideRecord(recorderRole: "orchestrator", audiences: ["architect"]);
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed", HeadBefore = HeadSha, HeadAfter = HeadSha, HeadSha = HeadSha, Facts = [],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));
        using var report = JsonDocument.Parse(writer.ToString());
        var guide = TempHost.FindSubcheck(report.RootElement, "guide-reachability");
        Assert.Equal("missing", guide.GetProperty("state").GetString());
        Assert.Equal("guide-record-absent", guide.GetProperty("cause").GetString());
        var evidence = Assert.Single(guide.GetProperty("evidence").EnumerateArray());
        Assert.Equal("orchestrator", evidence.GetProperty("role").GetString());
        Assert.Contains("required-role=architect; qualifies=false", evidence.GetProperty("provenance").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DigestValidatedLocalReviewIsRecordedEvidenceAndDoesNotSubstituteForPostedReview()
    {
        using var host = new TempHost();
        host.WriteMode(TeamMode.SoloConductor);
        host.WritePacketAndPublishArtifact();
        host.WriteReviewRecord();
        var reader = new FixedReader(new UnitStatusRemoteSnapshot
        {
            State = "completed",
            HeadBefore = HeadSha,
            HeadAfter = HeadSha,
            HeadSha = HeadSha,
            Facts =
            [
                new UnitStatusFact { Id = "delta-review", State = UnitStatusStates.Done, Cause = "delta-review-current-head" },
                new UnitStatusFact { Id = "posted-review", State = UnitStatusStates.Missing, Cause = "posted-review-not-recorded-current-head" },
            ],
        });
        using var writer = new StringWriter();

        Assert.Equal(0, UnitStatusCommand.ExecuteCore(host.Context,
            ["--execution-unit", "G855", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], writer, reader));

        using var document = JsonDocument.Parse(writer.ToString());
        var reviewStep = document.RootElement.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("id").GetString() == "independent-subagent-review");
        Assert.Equal("done", reviewStep.GetProperty("subchecks")[0].GetProperty("state").GetString());
        Assert.Equal("missing", reviewStep.GetProperty("subchecks")[1].GetProperty("state").GetString());
        var observed = Assert.Single(document.RootElement.GetProperty("observation").GetProperty("github_snapshot").GetProperty("reviews").EnumerateArray());
        Assert.Equal("local-cross-runtime-record", observed.GetProperty("source").GetString());
        Assert.Equal("request-changes", observed.GetProperty("verdict").GetString());
        Assert.Equal("cross-runtime", observed.GetProperty("relation").GetString());
    }

    private sealed class FixedReader : IUnitStatusSnapshotReader
    {
        private readonly UnitStatusRemoteSnapshot? _remote;
        private readonly UnitStatusClaimSnapshot? _claims;
        public FixedReader(UnitStatusRemoteSnapshot? remote = null, UnitStatusClaimSnapshot? claims = null)
        {
            _remote = remote;
            _claims = claims;
        }
        public int ClaimReads { get; private set; }
        public int GitHubReads { get; private set; }

        public UnitStatusClaimSnapshot ReadClaimSnapshot(CliContext context, string executionUnit)
        {
            ClaimReads++;
            return _claims ?? new UnitStatusClaimSnapshot { State = "completed" };
        }

        public UnitStatusRemoteSnapshot ObserveGitHub(CliContext context, string repo, int issue, int pullRequest,
            string executionUnit, string domain, string team)
        {
            GitHubReads++;
            return _remote ?? new UnitStatusRemoteSnapshot { State = "completed", Facts = [] };
        }

        public UnitStatusRemoteSnapshot ObserveGitHubIssue(CliContext context, string repo, int issue, string executionUnit)
        {
            GitHubReads++;
            return _remote ?? new UnitStatusRemoteSnapshot { State = "completed", Facts = [] };
        }
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _original = Directory.GetCurrentDirectory();

        public CurrentDirectoryScope(string path) => Directory.SetCurrentDirectory(path);

        public void Dispose() => Directory.SetCurrentDirectory(_original);
    }

    private sealed class TempHost : IDisposable
    {
        private readonly string _root;
        public string Root => _root;

        public TempHost(string? root = null)
        {
            _root = root ?? Path.Combine(Path.GetTempPath(), "g855-status-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, ".intent-cli"));
        }

        public CliContext Context => new()
        {
            RepoRoot = _root,
            Config = new CliConfig
            {
                Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", MetadataSourceBranch = "metadata" },
            },
        };

        public void WriteMode(string mode, string domain = "intent-cli", string team = "intent-cli-dev", string? previousMode = null)
        {
            var firstAt = DateTimeOffset.Parse("2026-09-30T00:00:00Z");
            var currentAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
            var transitions = previousMode is null
                ? new[] { new TeamModeTransition { From = TeamMode.Default, To = mode, At = currentAt } }
                : new[]
                {
                    new TeamModeTransition { From = TeamMode.Default, To = previousMode, At = firstAt },
                    new TeamModeTransition { From = previousMode, To = mode, At = currentAt },
                };
            var state = new TeamModeState
            {
                SchemaVersion = TeamModeStore.SchemaVersion,
                Entries =
                [
                    new TeamModeEntry
                    {
                        Domain = domain,
                        Team = team,
                        Mode = mode,
                        UpdatedAt = currentAt,
                        Transitions = transitions,
                    },
                ],
            };
            File.WriteAllText(TeamModeStore.ResolvePath(_root), JsonSerializer.Serialize(state));
        }

    public void WritePacketAndPublishArtifact(string? sourceArtifact = null, bool? knowledgeRequired = null,
        string? packetUnit = null, string? publishUnit = null, string? rulingArtifact = null, bool guideDeclared = false,
        string packetDomain = "intent-cli")
    {
        var directory = Path.Combine(_root, ".intent-cli", "issues", "G855");
        Directory.CreateDirectory(directory);
        var packetYaml = "schema_version: 1\nimplementation_issue_packet:\n  source_execution_unit: "
            + (packetUnit ?? "G855") + "\n  domain: " + packetDomain + "\n  target_repo: J-Tech-Japan/intent-system\n";
        if (sourceArtifact is not null) packetYaml += "  source_artifact: \"" + sourceArtifact + "\"\n";
        if (rulingArtifact is not null) packetYaml += "  ruling_artifact: \"" + rulingArtifact + "\"\n";
        if (knowledgeRequired is { } required)
            packetYaml += "knowledge_updates:\n  intent_tree:\n    required: " + required.ToString().ToLowerInvariant() + "\n    target_paths: []\n";
        if (guideDeclared)
            packetYaml += "guide_reachability:\n  no_role_facing_surface: false\n  routes:\n    - guide_surface: \"guide solo-conductor\"\n      role: architect\n      target_surface: \"unit status\"\n";
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), packetYaml);
            var artifact = new IssuePublishArtifact
            {
                ExecutionUnit = publishUnit ?? "G855",
                PublishStatus = "published",
                PacketPath = ".intent-cli/issues/G855/packet.yaml",
                IssueBodyPath = ".intent-cli/issues/G855/github-body.md",
                CreatedIssueNumber = 1862,
                CreatedIssueUrl = "https://github.com/J-Tech-Japan/intent-system/issues/1862",
                PublishedLabelName = "intent-target",
                LifecycleState = "pr-created",
                LinkedPrNumber = 1864,
                LinkedPrUrl = "https://github.com/J-Tech-Japan/intent-system/pull/1864",
            };
            File.WriteAllText(Path.Combine(directory, "publish.yaml"), IssuePublishArtifactYaml.Serialize(artifact));
        }

        public void WriteWritebackRecord(string? role, string fileName)
        {
            var directory = Path.Combine(_root, KnowledgeWriteBackRecord.RecordRootRelativePath, "G855");
            if (role is not null) directory = Path.Combine(directory, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName);
            Directory.CreateDirectory(directory);
            var record = new KnowledgeWriteBackRecord
            {
                ArtifactKind = KnowledgeWriteBackRecord.ArtifactKindValue,
                ExecutionUnit = "G855",
                Role = role,
                HostCommit = HeadSha,
                RecordedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                Targets = [],
            };
            File.WriteAllText(Path.Combine(directory, fileName), KnowledgeWriteBackRecord.Serialize(record));
        }

        public void WriteGuideRecord(string recorderRole, IReadOnlyList<string> audiences, bool roleSlot = false, string? fileName = null)
        {
            var directory = Path.Combine(_root, GuideReachabilityRecord.RecordRootRelativePath, "G855");
            if (roleSlot) directory = Path.Combine(directory, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName);
            Directory.CreateDirectory(directory);
            var record = new GuideReachabilityRecord
            {
                ArtifactKind = GuideReachabilityRecord.ArtifactKindValue,
                ExecutionUnit = "G855",
                Role = recorderRole,
                HostCommit = HeadSha,
                RecordedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                GuideSurfaces = ["guide solo-conductor"],
                Roles = audiences,
            };
            File.WriteAllText(Path.Combine(directory, fileName ?? "record.json"), GuideReachabilityRecord.Serialize(record));
        }

        public void WriteBugSourceChain(string bugId, string sourceIssueUrl, bool duplicate = false, bool corruptPlan = false,
            string? linkedIssueUrl = null, IReadOnlyList<string>? linkedUnits = null, IReadOnlyList<string>? resolvedPacketRefs = null)
        {
            var bugs = Path.Combine(_root, ".intent-cli", "bugs");
            Directory.CreateDirectory(bugs);
            var report = new BugReportArtifact
            {
                DomainSlug = "intent-cli",
                BugId = bugId,
                Title = "Source issue chain fixture",
                ReportSource = sourceIssueUrl,
                ProblemStatement = "A source issue for a command-level status test.",
                SuspectedFailureLocus = "status reader",
                OriginalInstructionRefs = [],
                AffectedIntentRefs = [],
                AffectedRuleSpecRefs = ["INTENT-1857-STATUS"],
                ClarificationCandidates = [],
                LinkedExecutionUnits = linkedUnits ?? [],
                LinkedIssueRefs = [linkedIssueUrl ?? sourceIssueUrl],
                LinkedPrRefs = [],
                LinkedReviewRefs = [],
            };
            var reportRef = BugReportArtifactPathResolver.Resolve(bugId);
            var triageRef = BugTriageArtifactPathResolver.Resolve(bugId);
            var planRef = BugExecutionArtifactPathResolver.Resolve(bugId);
            File.WriteAllText(Path.Combine(bugs, bugId + ".report.yaml"), BugReportArtifactYaml.Serialize(report));
            if (duplicate)
            {
                var second = report with { BugId = bugId + "-duplicate" };
                File.WriteAllText(Path.Combine(bugs, second.BugId + ".report.yaml"), BugReportArtifactYaml.Serialize(second));
            }
            var triage = new BugTriageArtifact
            {
                BugId = bugId,
                ReportRef = reportRef,
                TriageClassification = "rule-gap",
                DownstreamAction = "intent-only",
                ClarificationRequired = false,
                ClarificationReasons = [],
                OriginalInstructionRootRefs = ["INTENT-1857-STATUS"],
                LinkedReviewRefs = [],
                ResolvedExecutionUnits = [],
                ResolvedImplementationRefs = [],
                ResolvedReviewContextRefs = [],
                ResolvedPacketRefs = resolvedPacketRefs ?? [],
                UnresolvedExecutionUnits = [],
                ImplementationRepairCandidates = [],
                IntentRepairCandidates = ["INTENT-1857-STATUS"],
            };
            File.WriteAllText(Path.Combine(bugs, bugId + ".triage.yaml"), BugTriageArtifactYaml.Serialize(triage));
            var plan = new BugExecutionArtifact
            {
                BugId = bugId,
                ReportRef = reportRef,
                TriageRef = triageRef,
                DownstreamAction = "intent-only",
                ResolvedImplementationRefs = [],
                ResolvedReviewContextRefs = [],
                ResolvedPacketRefs = corruptPlan ? [".intent-cli/issues/G854/packet.yaml"] : resolvedPacketRefs ?? [],
                ImplementationTaskCandidates = [],
                IntentTaskCandidates = ["INTENT-1857-STATUS"],
                ClarificationRequired = false,
                ReadyToLaunch = false,
            };
            File.WriteAllText(Path.Combine(bugs, bugId + ".plan.yaml"), BugExecutionArtifactYaml.Serialize(plan));
        }

        public void WriteActualBugSourceChain(string bugId, string sourceIssueUrl)
        {
            var issueDirectory = Path.Combine(_root, ".intent-cli", "issues", "G855");
            File.WriteAllText(Path.Combine(issueDirectory, "implementation.md"), "# G855 implementation\n");
            File.WriteAllText(Path.Combine(issueDirectory, "review-context.md"), "# G855 review context\n");
            File.WriteAllText(Path.Combine(issueDirectory, "github-body.md"), "# G855 issue body\n");

            using var reportOutput = new StringWriter();
            Assert.Equal(0, BugReportCommand.Execute(Context,
                ["intent-cli", bugId, "--title", "G859 source report fixture", "--text", "A source issue report produced by the canonical writer.",
                    "--instruction-refs", "INTENT-1857-ADOPTION", "--affected-rule-spec-refs", "INTENT-1857-ADOPTION",
                    "--execution-units", "G855", "--issues", sourceIssueUrl], reportOutput));
            var reportPath = Path.Combine(_root, BugReportArtifactPathResolver.Resolve(bugId).Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(reportPath));
            var report = BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
            Assert.Equal("intent-cli", report.DomainSlug);
            Assert.Equal(bugId, report.BugId);
            Assert.Contains(sourceIssueUrl, report.LinkedIssueRefs, StringComparer.Ordinal);
            Assert.Contains("G855", report.LinkedExecutionUnits, StringComparer.Ordinal);

            using var triageOutput = new StringWriter();
            Assert.Equal(0, BugTriageCommand.Execute(Context, [bugId], triageOutput));
            var triagePath = Path.Combine(_root, BugTriageArtifactPathResolver.Resolve(bugId).Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(triagePath));
            var triage = BugTriageArtifactYaml.Deserialize(File.ReadAllText(triagePath));
            Assert.Equal(bugId, triage.BugId);
            Assert.Equal(BugReportArtifactPathResolver.Resolve(bugId), triage.ReportRef);
            Assert.False(triage.ClarificationRequired);
            Assert.Contains(".intent-cli/issues/G855/packet.yaml", triage.ResolvedPacketRefs, StringComparer.Ordinal);

            using var planOutput = new StringWriter();
            Assert.Equal(0, BugExecutionCommand.Execute(Context, [bugId], planOutput));
            var planPath = Path.Combine(_root, BugExecutionArtifactPathResolver.Resolve(bugId).Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(planPath));
            var plan = BugExecutionArtifactYaml.Deserialize(File.ReadAllText(planPath));
            Assert.Equal(bugId, plan.BugId);
            Assert.Equal(BugReportArtifactPathResolver.Resolve(bugId), plan.ReportRef);
            Assert.Equal(BugTriageArtifactPathResolver.Resolve(bugId), plan.TriageRef);
            Assert.True(plan.ReadyToLaunch);
            Assert.Contains(".intent-cli/issues/G855/packet.yaml", plan.ResolvedPacketRefs, StringComparer.Ordinal);
        }

        public string WriteActualBugReport(string bugId, string title, IReadOnlyList<string> issueRefs,
            IReadOnlyList<string> executionUnits)
        {
            var args = new List<string>
            {
                "intent-cli", bugId,
                "--title", title,
                "--text", "A canonical bug report emitted by the actual G859 writer fixture.",
                "--instruction-refs", "INTENT-1857-ADOPTION",
                "--affected-rule-spec-refs", "INTENT-1857-ADOPTION",
            };
            if (executionUnits.Count > 0)
            {
                args.Add("--execution-units");
                args.Add(string.Join(',', executionUnits));
            }
            if (issueRefs.Count > 0)
            {
                args.Add("--issues");
                args.Add(string.Join(',', issueRefs));
            }

            using var output = new StringWriter();
            Assert.Equal(0, BugReportCommand.Execute(Context, args.ToArray(), output));
            var reportRef = BugReportArtifactPathResolver.Resolve(bugId);
            var reportPath = Path.Combine(_root, reportRef.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(reportPath));
            var report = BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
            Assert.Equal("intent-cli", report.DomainSlug);
            Assert.Equal(bugId, report.BugId);
            Assert.Equal(title, report.Title);
            Assert.Equal(issueRefs, report.LinkedIssueRefs);
            Assert.Equal(executionUnits, report.LinkedExecutionUnits);
            return reportPath;
        }

        public void WriteReviewRecord()
        {
            var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                verdict = "request-changes",
                head_sha = HeadSha,
                blocking_findings = new[] { new { file = "src/Unit.cs", line = 1, scenario = "fixture finding" } },
                notes = Array.Empty<string>(),
            }));
            var record = new CrossRuntimeReviewRecord
            {
                ArtifactKind = CrossRuntimeReviewRecord.ArtifactKindValue,
                Repo = "J-Tech-Japan/intent-system",
                Pr = 1864,
                HeadSha = HeadSha,
                ExecutionUnit = "G855",
                Domain = "intent-cli",
                Team = "intent-cli-dev",
                Kind = CrossRuntimeReviewRecord.KindImplementation,
                Runtime = "codex",
                RuntimeVersion = "codex-test",
                ConductorRuntime = "claude",
                Relation = CrossRuntimeReviewRecord.RelationCrossRuntime,
                Verdict = "request-changes",
                BlockingFindings = [new CrossRuntimeReviewFinding { File = "src/Unit.cs", Line = 1, Scenario = "fixture finding" }],
                Notes = [],
                RecordedAt = DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
                RawVerdictFile = "",
                RawVerdictSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
            };
            record = record with { RawVerdictFile = CrossRuntimeReviewStore.RawRelativePath(record) };
            var result = CrossRuntimeReviewStore.Write(_root, record, raw);
            Assert.True(result.Written, result.Error);
        }

        internal static JsonElement FindSubcheck(JsonElement root, string factId) => root.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("subchecks").EnumerateArray())
            .Single(fact => fact.GetProperty("id").GetString() == factId);

        internal static UnitStatusReadAdapter CreateRealAdapter(TempHost host, ClaimRecord? active, IReadOnlyList<ClaimHistoryRecord> history) =>
            new(new FakeGitHub(), new ClaimSnapshotGit(active, history));

        internal static ClaimRecord Claim(string actor, DateTimeOffset claimedAt) => new(
            "1", "execution-unit:G855", actor, "intent-cli-dev", claimedAt, HeadSha);

        internal static ClaimHistoryRecord History(string operation, string actor, string displacedHolder,
            DateTimeOffset claimedAt, DateTimeOffset recordedAt) => new(
            "1", operation, "execution-unit:G855", actor, "intent-cli-dev", recordedAt, "fixture", displacedHolder,
            "intent-cli-dev", claimedAt, HeadSha);

        private sealed class ClaimSnapshotGit : IGitRemoteCommandRunner
        {
            private const string MetadataOid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            private readonly Dictionary<string, string> _documents;
            private readonly string _claimPath = ClaimCommand.ClaimPath("execution-unit:G855");
            private readonly string _historyDirectory;
            private readonly List<string> _paths = [];

            public ClaimSnapshotGit(ClaimRecord? active, IReadOnlyList<ClaimHistoryRecord> history)
            {
                _historyDirectory = $"{ClaimCommand.ClaimsDirectory}/history/{Path.GetFileNameWithoutExtension(_claimPath)}";
                _documents = new Dictionary<string, string>(StringComparer.Ordinal);
                if (active is not null)
                {
                    _documents[_claimPath] = JsonSerializer.Serialize(active);
                    _paths.Add(_claimPath);
                }
                for (var index = 0; index < history.Count; index++)
                {
                    var path = $"{_historyDirectory}/{index:D4}-{history[index].Operation}.json";
                    _documents[path] = JsonSerializer.Serialize(history[index]);
                    _paths.Add(path);
                }
            }

            public List<IReadOnlyList<string>> Calls { get; } = [];

            public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
            {
                var args = arguments.ToArray();
                Calls.Add(args);
                var output = args[0] switch
                {
                    "rev-parse" when args.Contains("--abbrev-ref", StringComparer.Ordinal) => "feature/unit",
                    "rev-parse" when args.Contains("HEAD", StringComparer.Ordinal) => HeadSha,
                    "rev-parse" => MetadataOid,
                    "ls-tree" => string.Join('\0', _paths) + (_paths.Count == 0 ? "" : "\0"),
                    "show" when args.Length == 2 && args[1].StartsWith(MetadataOid + ":", StringComparison.Ordinal)
                        && _documents.TryGetValue(args[1][(MetadataOid.Length + 1)..], out var value) => value,
                    _ => throw new InvalidOperationException("Unexpected bounded claim snapshot request: " + string.Join(' ', args)),
                };
                return new GitRemoteCommandResult { ExitCode = 0, StdOut = output, StdErr = "" };
            }
        }

        private sealed class FakeGitHub : IGitHubCommandRunner
        {
            public GitHubCommandResult Run(IReadOnlyList<string> arguments)
            {
                var endpoint = arguments[3];
                var json = endpoint switch
                {
                    "repos/J-Tech-Japan/intent-system/pulls/1864" => "{\"number\":1864,\"head\":{\"sha\":\"" + HeadSha + "\"},\"merged\":false,\"merge_commit_sha\":null,\"labels\":[]}",
                    "repos/J-Tech-Japan/intent-system/issues/1862" => "{\"number\":1862,\"labels\":[]}",
                    var path when path.StartsWith("repos/J-Tech-Japan/intent-system/pulls/1864/reviews?", StringComparison.Ordinal) => "[]",
                    var path when path.StartsWith("repos/J-Tech-Japan/intent-system/commits/" + HeadSha + "/check-runs?", StringComparison.Ordinal) => "{\"total_count\":0,\"check_runs\":[]}",
                    var path when path.StartsWith("repos/J-Tech-Japan/intent-system/commits/" + HeadSha + "/statuses?", StringComparison.Ordinal) => "[]",
                    _ => throw new InvalidOperationException("Unexpected bounded status request: " + endpoint),
                };
                return new GitHubCommandResult { ExitCode = 0, StdOut = json, StdErr = "" };
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
