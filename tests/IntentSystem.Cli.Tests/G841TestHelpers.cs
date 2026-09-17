using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>G841: shared fixtures, base literals and unreadable-packet seam helpers.</summary>
internal static class G841TestHelpers
{
    internal const string Domain = "intent-cli";
    internal const string Team = "intent-cli-dev";
    internal const string Repo = "J-Tech-Japan/intent-system";
    internal const string ProbeRepo = "J-Tech-Japan/probe";
    internal const string AlphaDomain = "alpha";
    internal const string BetaDomain = "beta";

    internal const string QuotedHashLine = "  source_artifact: \"review of PR #1823\"\n";
    internal const string PlainColonLine = "  target_part: retire the reader: all eleven sites\n";
    internal const string UnparseableYaml = "implementation_issue_packet:\n  domain: intent-cli\n  broken: [\n";

    internal const string UnparseableParserMessage =
        "While parsing a node, did not find expected node content.";

    internal const string UnparseableCrossRuntimeParseDetailG841 =
        "packet '.intent-cli/issues/G841/packet.yaml' could not be parsed at line 4, column 1: While parsing a node, did not find expected node content.";

    internal const string UnparseableCrossRuntimeParseDetailG841Pf =
        "packet '.intent-cli/issues/G841PF/packet.yaml' could not be parsed at line 4, column 1: While parsing a node, did not find expected node content.";

    internal const string DuplicateKeyCrossRuntimeParseDetailG841 =
        "packet '.intent-cli/issues/G841/packet.yaml' could not be parsed at line 3, column 3: Duplicate key domain";

    internal const string BlockStyleDependenciesPacket =
        """
        implementation_issue_packet:
          issue_title: "G841 fixture title"
          domain: intent-cli
          target_repo: submodules/intent-system
          dependencies:
            - G815
            - G823
        """;

    internal const string UnterminatedFlowSequenceYaml =
        """
        implementation_issue_packet:
          domain: intent-cli
          target_repo: J-Tech-Japan/intent-system
          dependencies: [G1, G2
        knowledge_updates:
          intent_tree:
            required: true
        """;

    // Base refusal literals captured from ba496314 (section 3 surfaces, non-packet branches).
    internal const string BaseTeamUnresolvedCause = "cross-runtime-review-team-unresolved";
    internal const string BaseQueueLinkageMissing = "queue-linkage";
    internal const string BasePacketDomainMissing = "packet-domain";
    internal const string BaseClaimTeamMissing = "claim-team";

    internal static string LegacyEquivalentPacket(string domain = Domain, string? extra = null) =>
        $"""
        implementation_issue_packet:
          issue_title: "G841 fixture title"
          domain: {domain}
          target_repo: J-Tech-Japan/intent-system
        """ + (extra ?? string.Empty);

    internal static string MinimalContractBody(string title = "G841 fixture title") =>
        $"""
        # {title}

        ## Goal
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
        - x

        ## Out Of Scope
        - x

        ## Acceptance Criteria
        - x

        ## Verification
        x

        ## Related Links
        - x

        ## Base Branch Policy
        Policy: `direct-main`
        Expected PR base branch: `main`
        Open all child PRs against `main` directly.
        """;

    internal static CliContext GatedContext(string root, string? heldTeam = Team) =>
        new()
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = Domain,
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
                CrossRuntimeReview = new CrossRuntimeReviewConfig
                {
                    Teams =
                    [
                        new CrossRuntimeReviewTeamDeclaration
                        {
                            Team = $"{Domain}/{Team}",
                            ConductorRuntime = "codex",
                            Repos = [Repo],
                        },
                    ],
                },
            },
        };

    internal static CliContext UngatedContext(string root) =>
        new()
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = Domain,
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
                CrossRuntimeReview = new CrossRuntimeReviewConfig(),
            },
        };

    internal static void WriteHostConfig(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        File.WriteAllText(
            Path.Combine(root, ".intent-cli", "config.toml"),
            "default_domain = \"intent-cli\"\nartifact_root = \".intent-cli\"\n");
    }

    internal static void WriteClaim(string root, string unit, string team, string actor = "design")
    {
        var claimPath = Path.Combine(root, ClaimCommand.ClaimPath($"execution-unit:{unit}").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
        File.WriteAllText(claimPath, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            scope = $"execution-unit:{unit}",
            actor,
            team,
            claimed_at = DateTimeOffset.UtcNow,
            base_commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        }));
    }

    internal static string PacketPath(string root, string unit) =>
        Path.Combine(root, ".intent-cli", "issues", unit, "packet.yaml");

    internal static string PacketDir(string root, string unit) =>
        Path.Combine(root, ".intent-cli", "issues", unit);

    internal static void WritePacketFiles(string root, string unit, string yaml, bool withContractBody = true, string bodyTitle = "G841 fixture title")
    {
        var directory = PacketDir(root, unit);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), yaml);
        File.WriteAllText(Path.Combine(directory, "review-context.md"), "# review\n");
        File.WriteAllText(Path.Combine(directory, "implementation.md"), "# notes\n");
        if (withContractBody)
        {
            File.WriteAllText(Path.Combine(directory, "github-body.md"), MinimalContractBody(bodyTitle));
        }
    }

    internal static IDisposable UnreadablePacket(
        string packetPath,
        Action armDeniedReader,
        Action disarmDeniedReader,
        out bool usedInjection)
    {
        usedInjection = false;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(packetPath, UnixFileMode.None);
        }

        if (OperatingSystem.IsWindows() || CanRead(packetPath))
        {
            armDeniedReader();
            usedInjection = true;
            return new UnreadablePacketScope(packetPath, disarmDeniedReader, restoreReader: true);
        }

        return new UnreadablePacketScope(packetPath, disarmDeniedReader, restoreReader: false);
    }

    internal static bool CanRead(string path)
    {
        try
        {
            using var _ = File.OpenRead(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void RestorePacketPermissions(string packetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(packetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    internal static void DeleteDirectoryBestEffort(string path, int maxAttempts = 5)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                if (!Directory.Exists(path))
                {
                    return;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Thread.Sleep(50 * (attempt + 1));
        }
    }

    internal static bool IsNonRootUnixUser()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        var idInfo = new System.Diagnostics.ProcessStartInfo("id")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        idInfo.ArgumentList.Add("-u");
        using var idProcess = System.Diagnostics.Process.Start(idInfo)!;
        var effectiveUserId = idProcess.StandardOutput.ReadToEnd().Trim();
        idProcess.WaitForExit();
        return idProcess.ExitCode == 0 && !string.Equals(effectiveUserId, "0", StringComparison.Ordinal);
    }

    internal static string ExpectedPublishFlowParseDetail(string packetPath, bool changedAfterFirstRead = false)
    {
        var prefix = changedAfterFirstRead
            ? $"packet '{packetPath}' changed after it was first read and could not be parsed"
            : $"packet '{packetPath}' could not be parsed";
        return $"{prefix} at line 4, column 1: {UnparseableParserMessage}";
    }

    internal static void AssertCrossRuntimeParseRefusal(
        JsonElement json,
        string expectedDetail,
        string relativePath = ".intent-cli/issues/G841/packet.yaml")
    {
        Assert.Equal(CrossRuntimeReviewCauses.PacketInvalid, json.GetProperty("cause").GetString());
        Assert.Equal("packet-invalid", json.GetProperty("resolution").GetProperty("missing").GetString());
        Assert.Equal(expectedDetail, json.GetProperty("detail").GetString());
        Assert.Equal(expectedDetail, json.GetProperty("resolution").GetProperty("detail").GetString());
        Assert.DoesNotContain("is not valid YAML", expectedDetail, StringComparison.Ordinal);
        Assert.Contains(relativePath, expectedDetail, StringComparison.Ordinal);
        Assert.Contains("could not be parsed", expectedDetail, StringComparison.Ordinal);
        Assert.Contains("repair `.intent-cli/issues/", json.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    internal static void AssertCrossRuntimeReadRefusal(JsonElement json, string relativePath = ".intent-cli/issues/G841/packet.yaml")
    {
        Assert.Equal(CrossRuntimeReviewCauses.PacketUnreadable, json.GetProperty("cause").GetString());
        Assert.Equal("packet-unreadable", json.GetProperty("resolution").GetProperty("missing").GetString());
        var detail = json.GetProperty("detail").GetString()!;
        Assert.Contains(relativePath, detail, StringComparison.Ordinal);
        Assert.Contains("could not be read:", detail, StringComparison.Ordinal);
        Assert.Contains("make `.intent-cli/issues/", json.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    private sealed class UnreadablePacketScope(string packetPath, Action disarmDeniedReader, bool restoreReader) : IDisposable
    {
        public void Dispose()
        {
            if (restoreReader)
            {
                disarmDeniedReader();
            }

            RestorePacketPermissions(packetPath);
        }
    }
}
