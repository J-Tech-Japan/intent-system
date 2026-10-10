using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class RulingCommandTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-10T12:00:00Z");

    [Fact]
    public void CanonicalCodec_UsesFixedUtf8BytesAndRejectsInvalidShapes()
    {
        var input = Encoding.UTF8.GetBytes(RecordJson("R-CODEC", "2026-10-10T12:00:00.1Z", "[\"G862\"]", "\"Use <this> 日本語 😀\""));
        Assert.True(RulingArtifact.TryParse(input, false, out var record, out var cause, out var detail), $"{cause}: {detail}");
        var bytes = RulingArtifact.Serialize(record!);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        var canonicalText = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\\u003C", canonicalText, StringComparison.Ordinal);
        Assert.Contains("\\u65E5\\u672C\\u8A9E", canonicalText, StringComparison.Ordinal);
        Assert.Contains("\\uD83D\\uDE00", canonicalText, StringComparison.Ordinal);
        Assert.Equal("2026-10-10T12:00:00.1000000Z", record!.NormalizedRecordedAt);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), record.Sha256);

        var duplicateBaseline = Encoding.UTF8.GetBytes(RecordJson("R-DUP", "2026-10-10T12:00:00Z", "[\"G862\"]"));
        Assert.True(RulingArtifact.TryParse(duplicateBaseline, false, out _, out _, out _));
        var duplicateRootKey = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(duplicateBaseline)
            .Replace("  \"id\": \"R-DUP\",", "  \"id\": \"R-DUP\",\n  \"id\": \"R-DUP\",", StringComparison.Ordinal));
        Assert.False(RulingArtifact.TryParse(duplicateRootKey, false, out _, out var duplicateCause, out var duplicateDetail));
        Assert.Equal("ruling-invalid-json", duplicateCause);
        Assert.Contains("duplicate", duplicateDetail, StringComparison.OrdinalIgnoreCase);
        Assert.False(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(RecordJson("R-NULL", "2026-10-10T12:00:00Z", "null")), false, out _, out _, out _));
        Assert.False(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(RecordJson("R-TIME", "12:00")), false, out _, out _, out _));
        Assert.False(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(RecordJson("R-UNKNOWN", "2026-10-10T12:00:00Z").Replace("\"supersedes\": []", "\"supersedes\": [], \"unexpected\": true", StringComparison.Ordinal)), false, out _, out _, out _));
    }

    [Fact]
    public void MalformedUnicode_IsStructuredInvalidUtf8AndDoesNotEscapeParser()
    {
        var valid = RecordJson("R-UNICODE", "2026-10-10T12:00:00Z");
        foreach (var malformed in new[]
        {
            valid.Replace("Keep the bounded choice.", "\\uD800", StringComparison.Ordinal),
            valid.Replace("Keep the bounded choice.", "\\uDC00", StringComparison.Ordinal),
            valid.Replace("\"decision\"", "\"\\uD800\"", StringComparison.Ordinal),
            valid.Replace("\"decision\"", "\"\\uDC00\"", StringComparison.Ordinal),
        })
        {
            var threw = Record.Exception(() => RulingArtifact.TryParse(Encoding.UTF8.GetBytes(malformed), false, out _, out _, out _));
            Assert.Null(threw);
            Assert.False(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(malformed), false, out _, out var cause, out _));
            Assert.Equal("ruling-invalid-utf8", cause);
        }

        var supplementary = valid.Replace("Keep the bounded choice.", "Keep 😀.", StringComparison.Ordinal);
        Assert.True(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(supplementary), false, out _, out _, out _));
    }

    [Fact]
    public void RawMalformedUtf8_IsStructuredInvalidUtf8()
    {
        var valid = Encoding.UTF8.GetBytes(RecordJson("R-RAW-UTF8", "2026-10-10T12:00:00Z"));
        foreach (var needle in new[] { Encoding.UTF8.GetBytes("Keep the bounded choice."), Encoding.UTF8.GetBytes("\"decision\"") })
        {
            var offset = valid.AsSpan().IndexOf(needle);
            Assert.True(offset >= 0);
            var malformed = new byte[valid.Length + 1];
            valid.AsSpan(0, offset).CopyTo(malformed);
            malformed[offset] = 0xff;
            valid.AsSpan(offset + needle.Length).CopyTo(malformed.AsSpan(offset + 1));
            Assert.False(RulingArtifact.TryParse(malformed, false, out _, out var cause, out _));
            Assert.Equal("ruling-invalid-utf8", cause);
        }
    }

    [Fact]
    public void MalformedUtf8CommandInputAndStoredNeighbor_ReturnStructuredResultsInBothFormats()
    {
        static byte[] ReplaceWithInvalidUtf8(byte[] source, string needle)
        {
            var needleBytes = Encoding.UTF8.GetBytes(needle);
            var offset = source.AsSpan().IndexOf(needleBytes);
            Assert.True(offset >= 0);
            var result = new byte[source.Length - needleBytes.Length + 1];
            source.AsSpan(0, offset).CopyTo(result);
            result[offset] = 0xff;
            source.AsSpan(offset + needleBytes.Length).CopyTo(result.AsSpan(offset + 1));
            return result;
        }

        var validText = RecordJson("R-BAD-STORED", "2026-10-10T12:00:00Z");
        var valid = Encoding.UTF8.GetBytes(validText);
        var malformedPayloads = new[]
        {
            Encoding.UTF8.GetBytes(validText.Replace("Keep the bounded choice.", "\\uD800", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(validText.Replace("Keep the bounded choice.", "\\uDC00", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(validText.Replace("\"decision\"", "\"\\uD800\"", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(validText.Replace("\"decision\"", "\"\\uDC00\"", StringComparison.Ordinal)),
            ReplaceWithInvalidUtf8(valid, "Keep the bounded choice."),
            ReplaceWithInvalidUtf8(valid, "\"decision\""),
        };

        foreach (var malformed in malformedPayloads)
        foreach (var format in new[] { "json", "markdown" })
        {
            using (var inputWorkspace = new TemporaryWorkspace())
            {
                var inputPath = Path.Combine(inputWorkspace.Root, "invalid.json");
                File.WriteAllBytes(inputPath, malformed);
                var args = new[] { "ruling", "record", "--id", "R-BAD-STORED", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", format };
                var command = Invoke(CreateContext(inputWorkspace.Root), args);
                Assert.Equal(1, command.ExitCode);
                using var result = format == "json" ? JsonDocument.Parse(command.Output) : StructuredJsonFromMarkdown(command.Output);
                Assert.Equal("ruling-invalid-utf8", result.RootElement.GetProperty("cause").GetString());
                Assert.Equal("unavailable", result.RootElement.GetProperty("status").GetString());
                Assert.False(Directory.Exists(Path.Combine(inputWorkspace.Root, ".intent-cli")));
            }

            using (var storedWorkspace = new TemporaryWorkspace())
            {
                var store = new RulingArtifactStore(storedWorkspace.Root);
                WriteArtifact(store, Artifact("R-GOOD", T0, []));
                var badPath = Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), "R-BAD-STORED.json");
                File.WriteAllBytes(badPath, malformed);
                var command = Invoke(CreateContext(storedWorkspace.Root), ["ruling", "show", "R-GOOD", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format]);
                Assert.Equal(1, command.ExitCode);
                using var result = format == "json" ? JsonDocument.Parse(command.Output) : StructuredJsonFromMarkdown(command.Output);
                Assert.Equal("ruling-invalid-utf8", result.RootElement.GetProperty("cause").GetString());
                Assert.Equal("unavailable", result.RootElement.GetProperty("status").GetString());
                Assert.Equal(malformed, File.ReadAllBytes(badPath));
            }
        }
    }

    [Fact]
    public void CommandReferenceExample_IsAValidGoldenCanonicalArtifactInBothLanguages()
    {
        var root = LocateRepositoryRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "08-command-reference.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "08-command-reference.md"));
        const string heading = "canonical on-disk example";
        var englishExample = ExtractJsonFence(english, heading).Replace("\r\n", "\n", StringComparison.Ordinal);
        var japaneseExample = ExtractJsonFence(japanese, heading).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(englishExample, japaneseExample);

        var bytes = Encoding.UTF8.GetBytes(englishExample);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.True(RulingArtifact.TryParse(bytes, requireRecordedAt: true, out var record, out var cause, out var detail), $"{cause}: {detail}");
        Assert.Equal("operator", record!.AuthorityRole);
        Assert.Equal("repository", record.ScopeKind);
        Assert.Equal(englishExample, Encoding.UTF8.GetString(RulingArtifact.Serialize(record)));
    }

    [Fact]
    public void RecordDryRunAndWrite_UseActualRouterWriter_ThenShowValidateAndIdempotentReplay()
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.Write("candidate.json", RecordJson("R-ONE", "2026-10-10T11:00:00Z"));
        var context = CreateContext(workspace.Root);

        var dry = Invoke(context, ["ruling", "record", "--id", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--format", "json"]);
        Assert.True(dry.ExitCode == 0, dry.Output);
        using (var result = JsonDocument.Parse(dry.Output))
        {
            Assert.Equal("preview", result.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("supplied-not-authenticated", result.RootElement.GetProperty("authority_status").GetString());
            Assert.Equal("not-verified", result.RootElement.GetProperty("publication_status").GetString());
            Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Null(result.RootElement.GetProperty("artifact_path").GetString());
            Assert.NotNull(result.RootElement.GetProperty("planned_artifact_path").GetString());
        }
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));

        var written = Invoke(context, ["ruling", "record", "--id", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(0, written.ExitCode);
        using var writtenJson = JsonDocument.Parse(written.Output);
        Assert.Equal("written", writtenJson.RootElement.GetProperty("disposition").GetString());
        Assert.True(writtenJson.RootElement.GetProperty("wrote").GetBoolean());
        var expectedPath = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-ONE.json");
        Assert.True(RulingArtifact.TryParse(File.ReadAllBytes(inputPath), false, out var parsed, out _, out _));
        var canonicalBytes = RulingArtifact.Serialize(parsed!);
        var persistedBytes = File.ReadAllBytes(expectedPath);
        Assert.Equal(canonicalBytes, persistedBytes);

        var show = Invoke(context, ["ruling", "show", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
        Assert.Equal(0, show.ExitCode);
        using var showJson = JsonDocument.Parse(show.Output);
        Assert.Equal("shown", showJson.RootElement.GetProperty("disposition").GetString());
        Assert.Equal("active", showJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, showJson.RootElement.GetProperty("timestamp_source").ValueKind);
        Assert.Equal("validated", JsonDocument.Parse(Invoke(context, ["ruling", "validate", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]).Output).RootElement.GetProperty("disposition").GetString());

        var showMarkdown = Invoke(context, ["ruling", "show", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "markdown"]);
        Assert.Equal(0, showMarkdown.ExitCode);
        using (var markdownJson = StructuredJsonFromMarkdown(showMarkdown.Output))
        {
            Assert.Equal(showJson.RootElement.GetProperty("disposition").GetString(), markdownJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal(showJson.RootElement.GetProperty("status").GetString(), markdownJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(showJson.RootElement.GetProperty("content_sha256").GetString(), markdownJson.RootElement.GetProperty("content_sha256").GetString());
            Assert.Equal(showJson.RootElement.GetProperty("artifact_path").GetString(), markdownJson.RootElement.GetProperty("artifact_path").GetString());
        }
        var validateMarkdown = Invoke(context, ["ruling", "validate", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "markdown"]);
        Assert.Equal(0, validateMarkdown.ExitCode);
        using (var markdownJson = StructuredJsonFromMarkdown(validateMarkdown.Output))
        {
            Assert.Equal("validated", markdownJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("active", markdownJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(showJson.RootElement.GetProperty("content_sha256").GetString(), markdownJson.RootElement.GetProperty("content_sha256").GetString());
        }

        var replay = Invoke(context, ["ruling", "record", "--id", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--format", "json"]);
        Assert.Equal(0, replay.ExitCode);
        using var replayJson = JsonDocument.Parse(replay.Output);
        Assert.Equal("idempotent", replayJson.RootElement.GetProperty("disposition").GetString());
        Assert.False(replayJson.RootElement.GetProperty("wrote").GetBoolean());
        Assert.Equal(JsonValueKind.Null, replayJson.RootElement.GetProperty("planned_artifact_path").ValueKind);
        Assert.Equal(expectedPath.Replace(workspace.Root + Path.DirectorySeparatorChar, "", StringComparison.Ordinal).Replace(Path.DirectorySeparatorChar, '/'), replayJson.RootElement.GetProperty("artifact_path").GetString());
        Assert.Equal(persistedBytes, File.ReadAllBytes(expectedPath));

        var replayMarkdown = Invoke(context, ["ruling", "record", "--id", "R-ONE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--format", "markdown"]);
        Assert.Equal(0, replayMarkdown.ExitCode);
        Assert.DoesNotContain("Planned artifact:", replayMarkdown.Output, StringComparison.Ordinal);
        using var replayMarkdownJson = StructuredJsonFromMarkdown(replayMarkdown.Output);
        Assert.Equal(JsonValueKind.Null, replayMarkdownJson.RootElement.GetProperty("planned_artifact_path").ValueKind);
        Assert.Equal(replayJson.RootElement.GetProperty("artifact_path").GetString(), replayMarkdownJson.RootElement.GetProperty("artifact_path").GetString());
    }

    [Fact]
    public void GeneratedTimestampReplayAndReaderClockSkew_PreserveTheFirstArtifact()
    {
        using var workspace = new TemporaryWorkspace();
        var context = CreateContext(workspace.Root);
        var input = RecordJson("R-GENERATED", "2026-10-10T11:00:00Z")
            .Replace("  \"recorded_at\": \"2026-10-10T11:00:00Z\",\n", "", StringComparison.Ordinal);
        var inputPath = workspace.Write("generated.json", input);

        var preview = Invoke(context, ["ruling", "record", "--id", "R-GENERATED", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--format", "json"]);
        Assert.Equal(0, preview.ExitCode);
        using (var result = JsonDocument.Parse(preview.Output))
        {
            Assert.Equal("generated", result.RootElement.GetProperty("timestamp_source").GetString());
            Assert.NotNull(result.RootElement.GetProperty("normalized_record").GetProperty("recorded_at").GetString());
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("recovery_hint").ValueKind);
            Assert.Contains("proposal", result.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));

        var firstWrite = Invoke(context, ["ruling", "record", "--id", "R-GENERATED", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(0, firstWrite.ExitCode);
        using var firstJson = JsonDocument.Parse(firstWrite.Output);
        Assert.Equal("generated", firstJson.RootElement.GetProperty("timestamp_source").GetString());
        var path = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-GENERATED.json");
        var original = File.ReadAllBytes(path);
        Assert.True(RulingArtifact.TryParse(original, true, out var stored, out var cause, out var detail), $"{cause}: {detail}");
        Assert.True(new RulingArtifactStore(workspace.Root).Evaluate("intent-cli", "intent-cli-dev", "R-GENERATED", T0).Status == "active");

        var replay = Invoke(context, ["ruling", "record", "--id", "R-GENERATED", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--format", "json"]);
        Assert.Equal(0, replay.ExitCode);
        using var replayJson = JsonDocument.Parse(replay.Output);
        Assert.Equal("idempotent", replayJson.RootElement.GetProperty("disposition").GetString());
        Assert.Equal("existing", replayJson.RootElement.GetProperty("timestamp_source").GetString());
        Assert.Equal(stored!.Sha256, replayJson.RootElement.GetProperty("content_sha256").GetString());
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void CommandClockOverrides_PreserveGeneratedReplayAndAllowEarlierReadOfLaterWrite()
    {
        using var workspace = new TemporaryWorkspace();
        var context = CreateContext(workspace.Root);
        var generatedPath = workspace.Write("generated.json", RecordJson("R-GENERATED-REPLAY", "2026-10-10T12:00:00Z")
            .Replace("  \"recorded_at\": \"2026-10-10T12:00:00Z\",\n", "", StringComparison.Ordinal));
        var generatedArgs = new[] { "ruling", "record", "--id", "R-GENERATED-REPLAY", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", generatedPath, "--authority-role", "operator", "--format", "json" };
        var preview = InvokeRecordAt(context, generatedArgs, T0);
        Assert.Equal(0, preview.ExitCode);
        using (var previewJson = JsonDocument.Parse(preview.Output))
        {
            Assert.Equal("preview", previewJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("generated", previewJson.RootElement.GetProperty("timestamp_source").GetString());
            Assert.Null(previewJson.RootElement.GetProperty("recovery_hint").GetString());
            Assert.Contains("proposal", previewJson.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
            var explicitGenerated = workspace.Write("generated-normalized.json", previewJson.RootElement.GetProperty("normalized_record").GetRawText());
            var explicitArgs = generatedArgs.Select(value => value == generatedPath ? explicitGenerated : value).Append("--write").ToArray();
            var replayFromPreview = InvokeRecordAt(context, explicitArgs, T0.AddMinutes(1));
            Assert.Equal(0, replayFromPreview.ExitCode);
            using var writeJson = JsonDocument.Parse(replayFromPreview.Output);
            Assert.Equal("written", writeJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("supplied", writeJson.RootElement.GetProperty("timestamp_source").GetString());
        }

        var laterPath = workspace.Write("later.json", RecordJson("R-CLOCK-SKEW", "2026-10-10T12:01:00Z"));
        var laterArgs = new[] { "ruling", "record", "--id", "R-CLOCK-SKEW", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", laterPath, "--authority-role", "operator", "--write", "--format", "json" };
        var laterWrite = InvokeRecordAt(context, laterArgs, T0.AddMinutes(2));
        Assert.Equal(0, laterWrite.ExitCode);
        var targetPath = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-CLOCK-SKEW.json");
        var firstBytes = File.ReadAllBytes(targetPath);
        var digest = Convert.ToHexString(SHA256.HashData(firstBytes)).ToLowerInvariant();

        foreach (var (operation, args, expectedDisposition) in new[]
        {
            ("show", new[] { "ruling", "show", "R-CLOCK-SKEW", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json" }, "shown"),
            ("validate", new[] { "ruling", "validate", "R-CLOCK-SKEW", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json" }, "validated"),
        })
        {
            var read = InvokeReadAt(context, operation, args, T0);
            Assert.True(read.ExitCode == 0, read.Output);
            using var readJson = JsonDocument.Parse(read.Output);
            Assert.Equal(expectedDisposition, readJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("active", readJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, readJson.RootElement.GetProperty("timestamp_source").ValueKind);
            Assert.Equal(digest, readJson.RootElement.GetProperty("content_sha256").GetString());
        }

        var earlierReplay = InvokeRecordAt(context, laterArgs.Where(value => value != "--write").ToArray(), T0);
        Assert.Equal(0, earlierReplay.ExitCode);
        using (var replayJson = JsonDocument.Parse(earlierReplay.Output))
        {
            Assert.Equal("idempotent", replayJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("supplied", replayJson.RootElement.GetProperty("timestamp_source").GetString());
            Assert.Equal(digest, replayJson.RootElement.GetProperty("content_sha256").GetString());
        }
        Assert.Equal(firstBytes, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void OversizedCommandInputIsRefusedInBothFormatsWithoutCreatingState()
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var workspace = new TemporaryWorkspace();
            var oversized = Path.Combine(workspace.Root, "oversized.json");
            File.WriteAllBytes(oversized, new byte[RulingArtifact.MaximumBytes + 1]);
            var args = new[] { "ruling", "record", "--id", "R-TOO-LARGE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", oversized, "--authority-role", "operator", "--write", "--format", format };
            var output = Invoke(CreateContext(workspace.Root), args);
            Assert.Equal(1, output.ExitCode);
            using var result = format == "json" ? JsonDocument.Parse(output.Output) : StructuredJsonFromMarkdown(output.Output);
            Assert.Equal("refused", result.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("unavailable", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("ruling-size-limit", result.RootElement.GetProperty("cause").GetString());
            Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
            Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
        }
    }

    [Fact]
    public void ActualCommandPostLinkCleanupAndReadbackFaultsRemainTruthfulInBothFormats()
    {
        foreach (var format in new[] { "json", "markdown" })
        foreach (var phase in new[] { "owned-temp-cleanup", "readback" })
        {
            using var workspace = new TemporaryWorkspace();
            var id = "R-COMMAND-" + (phase == "readback" ? "READBACK" : "CLEANUP");
            var inputPath = workspace.Write("candidate.json", RecordJson(id, "2026-10-10T12:00:00Z"));
            Assert.True(RulingArtifact.TryParse(File.ReadAllBytes(inputPath), false, out var parsed, out var parseCause, out var parseDetail), $"{parseCause}: {parseDetail}");
            var canonical = RulingArtifact.Serialize(parsed!);
            var finalPath = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", id + ".json");
            string? ownedTemp = null;
            var reached = false;
            var args = new[] { "ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", format };
            using var writer = new StringWriter();
            var exitCode = RulingCommand.ExecuteRecord(CreateContext(workspace.Root), args[2..], writer, (operation, path) =>
            {
                if (operation != phase) return;
                reached = true;
                if (phase == "owned-temp-cleanup") ownedTemp = path;
                Assert.Equal(canonical, File.ReadAllBytes(finalPath));
                throw new IOException("reached command " + phase + " sentinel");
            }, T0);

            Assert.True(reached);
            Assert.Equal(1, exitCode);
            using var result = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString());
            var root = result.RootElement;
            Assert.Equal("unavailable", root.GetProperty("disposition").GetString());
            Assert.Equal("unavailable", root.GetProperty("status").GetString());
            Assert.Equal(phase == "readback" ? "ruling-readback-failed" : "ruling-temp-cleanup-failed", root.GetProperty("cause").GetString());
            Assert.True(root.GetProperty("wrote").GetBoolean());
            Assert.Equal("not-verified", root.GetProperty("publication_status").GetString());
            Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/" + id + ".json", root.GetProperty("artifact_path").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant(), root.GetProperty("content_sha256").GetString());
            Assert.Equal(canonical, File.ReadAllBytes(finalPath));
            if (phase == "owned-temp-cleanup")
            {
                Assert.NotNull(ownedTemp);
                Assert.True(File.Exists(ownedTemp));
                Assert.Equal(new[] { finalPath, ownedTemp }.Order(StringComparer.Ordinal), Directory.GetFiles(Path.GetDirectoryName(finalPath)!).Order(StringComparer.Ordinal));
            }
            else
            {
                Assert.Equal(new[] { finalPath }, Directory.GetFiles(Path.GetDirectoryName(finalPath)!));
            }
        }
    }

    [Fact]
    public void ImmutableIdReplay_IdenticalIsIdempotentAndChangedContentConflictsWithoutOverwrite()
    {
        using var workspace = new TemporaryWorkspace();
        var context = CreateContext(workspace.Root);
        var input = workspace.Write("candidate.json", RecordJson("R-IMMUTABLE", "2026-10-10T11:00:00Z"));
        var write = Invoke(context, ["ruling", "record", "--id", "R-IMMUTABLE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(0, write.ExitCode);
        var target = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-IMMUTABLE.json");
        var original = File.ReadAllBytes(target);

        var baseline = RecordJson("R-IMMUTABLE", "2026-10-10T11:00:00Z");
        var changedCases = new (string Name, string Json, string Cause)[]
        {
            ("decision", RecordJson("R-IMMUTABLE", "2026-10-10T11:00:00Z", decision: "\"A different decision.\""), "ruling-content-conflict"),
            ("rationale", RecordJson("R-IMMUTABLE", "2026-10-10T11:00:00Z", rationale: "\"A different rationale.\""), "ruling-content-conflict"),
            ("authority", baseline.Replace("\"authority_role\": \"operator\"", "\"authority_role\": \"architect\"", StringComparison.Ordinal), "ruling-invalid-authority"),
            ("domain", baseline.Replace("\"domain\": \"intent-cli\"", "\"domain\": \"other-domain\"", StringComparison.Ordinal), "ruling-identity-conflict"),
            ("team", baseline.Replace("\"team\": \"intent-cli-dev\"", "\"team\": \"other-team\"", StringComparison.Ordinal), "ruling-identity-conflict"),
            ("repository", baseline.Replace("J-Tech-Japan/intent-system", "other-owner/other-repo", StringComparison.Ordinal), "ruling-content-conflict"),
            ("recorded-at", RecordJson("R-IMMUTABLE", "2026-10-10T11:00:01Z"), "ruling-content-conflict"),
        };
        foreach (var (name, json, expectedCause) in changedCases)
        {
            var changedPath = workspace.Write(name + ".json", json);
            var conflict = Invoke(context, ["ruling", "record", "--id", "R-IMMUTABLE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", changedPath, "--authority-role", "operator", "--write", "--format", "json"]);
            Assert.Equal(1, conflict.ExitCode);
            using var conflictJson = JsonDocument.Parse(conflict.Output);
            Assert.Equal(expectedCause is "ruling-content-conflict" or "ruling-identity-conflict" ? "conflict" : "unavailable", conflictJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(expectedCause, conflictJson.RootElement.GetProperty("cause").GetString());
            Assert.False(conflictJson.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Equal(original, File.ReadAllBytes(target));
        }
    }

    [Fact]
    public void InvalidPayloadShapes_ReturnStructuredRefusalsBeforeCreatingRulingState()
    {
        var valid = RecordJson("R-INVALID", "2026-10-10T11:00:00Z");
        var cases = new (string Name, string Json)[]
        {
            ("null-root", "null"),
            ("null-scope", valid.Replace("\"scope\": { \"target_repo\": \"J-Tech-Japan/intent-system\", \"kind\": \"execution-units\", \"execution_units\": [\"G862\"] }", "\"scope\": null", StringComparison.Ordinal)),
            ("null-units", valid.Replace("\"execution_units\": [\"G862\"]", "\"execution_units\": null", StringComparison.Ordinal)),
            ("null-unit", valid.Replace("\"execution_units\": [\"G862\"]", "\"execution_units\": [null]", StringComparison.Ordinal)),
            ("boolean-decision", valid.Replace("\"decision\": \"Keep the bounded choice.\"", "\"decision\": true", StringComparison.Ordinal)),
            ("number-rationale", valid.Replace("\"rationale\": \"The evidence supports this scope.\"", "\"rationale\": 7", StringComparison.Ordinal)),
            ("null-evidence", valid.Replace("\"evidence_refs\": [\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"]", "\"evidence_refs\": null", StringComparison.Ordinal)),
            ("empty-evidence", valid.Replace("\"evidence_refs\": [\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"]", "\"evidence_refs\": []", StringComparison.Ordinal)),
            ("duplicate-evidence", valid.Replace("\"evidence_refs\": [\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"]", "\"evidence_refs\": [\"same\", \"same\"]", StringComparison.Ordinal)),
            ("duplicate-unit", valid.Replace("\"execution_units\": [\"G862\"]", "\"execution_units\": [\"G862\", \"G862\"]", StringComparison.Ordinal)),
            ("unknown-nested", valid.Replace("\"scope\": { \"target_repo\": \"J-Tech-Japan/intent-system\",", "\"scope\": { \"unexpected\": false, \"target_repo\": \"J-Tech-Japan/intent-system\",", StringComparison.Ordinal)),
            ("null-supersedes", valid.Replace("\"supersedes\": []", "\"supersedes\": null", StringComparison.Ordinal)),
            ("missing-required", valid.Replace("\"schema_version\": \"1\",\n", "", StringComparison.Ordinal)),
            ("duplicate-nested-key", valid.Replace("\"kind\": \"execution-units\"", "\"kind\": \"execution-units\", \"kind\": \"repository\"", StringComparison.Ordinal)),
            ("bad-scope-kind", valid.Replace("\"kind\": \"execution-units\"", "\"kind\": \"all-repositories\"", StringComparison.Ordinal)),
            ("empty-execution-units", valid.Replace("\"execution_units\": [\"G862\"]", "\"execution_units\": []", StringComparison.Ordinal)),
            ("repository-url", valid.Replace("J-Tech-Japan/intent-system", "https://github.com/J-Tech-Japan/intent-system", StringComparison.Ordinal)),
            ("duplicate-supersedes", valid.Replace("\"supersedes\": []", "\"supersedes\": [\"R-A\", \"R-A\"]", StringComparison.Ordinal)),
        };

        foreach (var (name, json) in cases)
        {
            using var workspace = new TemporaryWorkspace();
            var path = workspace.Write(name + ".json", json);
            var result = Invoke(CreateContext(workspace.Root), ["ruling", "record", "--id", "R-INVALID", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", path, "--authority-role", "operator", "--write", "--format", "json"]);
            Assert.Equal(1, result.ExitCode);
            using var output = JsonDocument.Parse(result.Output);
            Assert.Equal("refused", output.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
            Assert.False(output.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Null(output.RootElement.GetProperty("content_sha256").GetString());
            Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
        }

        using var markdownWorkspace = new TemporaryWorkspace();
        var markdownInput = markdownWorkspace.Write("bad-scalar.json", valid.Replace("\"decision\": \"Keep the bounded choice.\"", "\"decision\": false", StringComparison.Ordinal));
        var markdownArgs = new[] { "ruling", "record", "--id", "R-INVALID", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", markdownInput, "--authority-role", "operator", "--write", "--format", "markdown" };
        var markdown = Invoke(CreateContext(markdownWorkspace.Root), markdownArgs);
        Assert.Equal(1, markdown.ExitCode);
        using var markdownResult = StructuredJsonFromMarkdown(markdown.Output);
        Assert.Equal("ruling-invalid-input", markdownResult.RootElement.GetProperty("cause").GetString());
        Assert.Equal("unavailable", markdownResult.RootElement.GetProperty("status").GetString());
        Assert.False(markdownResult.RootElement.GetProperty("wrote").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(markdownWorkspace.Root, ".intent-cli")));
    }

    [Fact]
    public void StrictPayloadShapeMatrix_UsesSingleMutationsAndRefusesBothFormatsWithoutWrites()
    {
        var timestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        const string id = "R-S04-MATRIX";
        var valid = RecordJson(id, timestamp);
        var validBytes = Encoding.UTF8.GetBytes(valid);
        Assert.True(RulingArtifact.TryParse(validBytes, false, out _, out _, out _));

        using (var previewWorkspace = new TemporaryWorkspace())
        {
            var path = previewWorkspace.Write("valid-execution-units.json", valid);
            var preview = Invoke(CreateContext(previewWorkspace.Root), ["ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", path, "--authority-role", "operator", "--format", "json"]);
            Assert.Equal(0, preview.ExitCode);
            using var output = JsonDocument.Parse(preview.Output);
            Assert.Equal("preview", output.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("active", output.RootElement.GetProperty("status").GetString());
            Assert.False(output.RootElement.GetProperty("wrote").GetBoolean());
            Assert.False(Directory.Exists(Path.Combine(previewWorkspace.Root, ".intent-cli")));
        }

        var rows = new List<(string Name, string Json, string Cause, string Detail)>();
        void Add(string name, string json, string detail, string cause = "ruling-invalid-input")
        {
            Assert.NotEqual(valid, json);
            rows.Add((name, json, cause, detail));
        }
        string ReplaceOnce(string token, string replacement)
        {
            var index = valid.IndexOf(token, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Missing single-mutation token: {token}");
            Assert.Equal(index, valid.LastIndexOf(token, StringComparison.Ordinal));
            return valid[..index] + replacement + valid[(index + token.Length)..];
        }

        foreach (var (name, json) in new[]
        {
            ("root-null", "null"), ("root-array", "[]"), ("root-string", "\"scalar\""), ("root-boolean", "false"), ("root-number", "7"),
        }) Add(name, json, "root must be an object");

        const string scopeToken = "\"scope\": { \"target_repo\": \"J-Tech-Japan/intent-system\", \"kind\": \"execution-units\", \"execution_units\": [\"G862\"] }";
        foreach (var (name, json) in new[]
        {
            ("scope-null", "null"), ("scope-array", "[]"), ("scope-string", "\"scope\""), ("scope-boolean", "false"), ("scope-number", "7"),
        }) Add(name, ReplaceOnce(scopeToken, "\"scope\": " + json), "scope must be an object");

        var scalarFields = new (string Name, string Token, string Key, string Detail)[]
        {
            ("schema-version", "\"schema_version\": \"1\"", "schema_version", "schema_version must be the string '1'"),
            ("id", "\"id\": \"R-S04-MATRIX\"", "id", "id must be a canonical identifier"),
            ("domain", "\"domain\": \"intent-cli\"", "domain", "domain must be a canonical identifier"),
            ("team", "\"team\": \"intent-cli-dev\"", "team", "team must be a canonical identifier"),
            ("authority-role", "\"authority_role\": \"operator\"", "authority_role", "authority_role must be a string"),
            ("scope-target-repo", "\"target_repo\": \"J-Tech-Japan/intent-system\"", "target_repo", "scope.target_repo must be owner/repository"),
            ("scope-kind", "\"kind\": \"execution-units\"", "kind", "scope.kind must be repository or execution-units"),
            ("decision", "\"decision\": \"Keep the bounded choice.\"", "decision", "decision must be nonblank text"),
            ("rationale", "\"rationale\": \"The evidence supports this scope.\"", "rationale", "rationale must be nonblank text"),
            ("recorded-at", "\"recorded_at\": \"" + timestamp + "\"", "recorded_at", "recorded_at must be a full UTC timestamp"),
        };
        foreach (var (name, token, key, detail) in scalarFields)
        foreach (var invalidType in new[] { (Name: "null", Json: "null"), (Name: "object", Json: "{}"), (Name: "array", Json: "[]"), (Name: "boolean", Json: "false"), (Name: "number", Json: "7") })
            Add(name + "-" + invalidType.Name, ReplaceOnce(token, "\"" + key + "\": " + invalidType.Json), detail);

        foreach (var invalidType in new[] { (Name: "object", Json: "{}"), (Name: "array", Json: "[]"), (Name: "string", Json: "\"not-a-timestamp\""), (Name: "boolean", Json: "false"), (Name: "number", Json: "7") })
            Add("expires-at-" + invalidType.Name, ReplaceOnce("\"expires_at\": null", "\"expires_at\": " + invalidType.Json), "expires_at must be null or a full UTC timestamp");

        var arrayFields = new (string Name, string Token, string Key, string Detail)[]
        {
            ("execution-units", "\"execution_units\": [\"G862\"]", "execution_units", "scope.execution_units must be a string array of at most 128 entries"),
            ("evidence-refs", "\"evidence_refs\": [\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"]", "evidence_refs", "evidence_refs must contain 1–64 unique nonblank strings"),
            ("supersedes", "\"supersedes\": []", "supersedes", "supersedes must contain at most 500 unique canonical identifiers"),
        };
        foreach (var (name, token, key, detail) in arrayFields)
        foreach (var invalidType in new[] { (Name: "null", Json: "null"), (Name: "object", Json: "{}"), (Name: "string", Json: "\"scalar\""), (Name: "boolean", Json: "false"), (Name: "number", Json: "7") })
            Add(name + "-collection-" + invalidType.Name, ReplaceOnce(token, "\"" + key + "\": " + invalidType.Json), detail);

        var itemFields = new (string Name, string Token, string Key, string Detail, string ValidItem)[]
        {
            ("execution-unit", "\"execution_units\": [\"G862\"]", "execution_units", "scope.execution_units must be a string array of at most 128 entries", "G862"),
            ("evidence-ref", "\"evidence_refs\": [\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"]", "evidence_refs", "evidence_refs must contain 1–64 unique nonblank strings", "https://github.com/J-Tech-Japan/intent-system/issues/1885"),
            ("supersedes-item", "\"supersedes\": []", "supersedes", "supersedes must contain at most 500 unique canonical identifiers", "R-S04-PREVIOUS"),
        };
        foreach (var (name, token, key, detail, validItem) in itemFields)
        foreach (var invalidItem in new[] { (Name: "null", Json: "null"), (Name: "object", Json: "{}"), (Name: "array", Json: "[]"), (Name: "boolean", Json: "false"), (Name: "number", Json: "7") })
        {
            var collection = key == "supersedes" ? "[\"R-S04-PREVIOUS\"]" : "[\"" + validItem + "\"]";
            var currentToken = key == "supersedes" ? token : "\"" + key + "\": " + collection;
            Add(name + "-item-" + invalidItem.Name, ReplaceOnce(currentToken, "\"" + key + "\": [" + invalidItem.Json + "]"), detail);
        }

        Add("missing-required-key", ReplaceOnce("  \"schema_version\": \"1\",\n", ""), "required field 'schema_version' is missing");
        Add("unknown-root-key", ReplaceOnce("  \"supersedes\": []", "  \"supersedes\": [],\n  \"unexpected\": true"), "unknown field 'unexpected'");
        Add("unknown-scope-key", ReplaceOnce("\"scope\": { \"target_repo\":", "\"scope\": { \"unexpected\": true, \"target_repo\":"), "scope: unknown field 'unexpected'");
        Add("duplicate-root-key", ReplaceOnce("  \"id\": \"R-S04-MATRIX\",", "  \"id\": \"R-S04-MATRIX\",\n  \"id\": \"R-S04-MATRIX\","), "duplicate JSON object key", "ruling-invalid-json");
        Add("duplicate-scope-key", ReplaceOnce("\"kind\": \"execution-units\"", "\"kind\": \"execution-units\", \"kind\": \"repository\""), "duplicate JSON object key", "ruling-invalid-json");
        Add("duplicate-units", ReplaceOnce("\"execution_units\": [\"G862\"]", "\"execution_units\": [\"G862\", \"G862\"]"), "scope.execution_units contains an invalid or duplicate identifier");
        Add("duplicate-evidence-refs", ReplaceOnce("\"evidence_refs\": [\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"]", "\"evidence_refs\": [\"same\", \"same\"]"), "evidence_refs must contain 1–64 unique nonblank strings");
        Add("duplicate-supersedes", ReplaceOnce("\"supersedes\": []", "\"supersedes\": [\"R-S04-PREVIOUS\", \"R-S04-PREVIOUS\"]"), "supersedes must contain at most 500 unique canonical identifiers");
        Add("empty-evidence-refs", ReplaceOnce("\"evidence_refs\": [\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"]", "\"evidence_refs\": []"), "evidence_refs must contain 1–64 unique nonblank strings");
        Add("empty-execution-units", ReplaceOnce("\"execution_units\": [\"G862\"]", "\"execution_units\": []"), "execution-units scope requires at least one unit");

        var repositoryEmptyUnits = ReplaceOnce("\"kind\": \"execution-units\", \"execution_units\": [\"G862\"]", "\"kind\": \"repository\", \"execution_units\": []");
        Assert.True(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(repositoryEmptyUnits), false, out _, out _, out _));
        using (var repositoryWorkspace = new TemporaryWorkspace())
        {
            var repositoryInput = repositoryWorkspace.Write("repository.json", repositoryEmptyUnits);
            var repositoryNow = DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture).AddSeconds(1);
            var repositoryPreview = InvokeRecordAt(CreateContext(repositoryWorkspace.Root),
                ["ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", repositoryInput, "--authority-role", "operator", "--format", "json"], repositoryNow);
            Assert.True(repositoryPreview.ExitCode == 0, repositoryPreview.Output);
            using var output = JsonDocument.Parse(repositoryPreview.Output);
            Assert.Equal("preview", output.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("repository", output.RootElement.GetProperty("normalized_record").GetProperty("scope").GetProperty("kind").GetString());
            Assert.Empty(output.RootElement.GetProperty("normalized_record").GetProperty("scope").GetProperty("execution_units").EnumerateArray());
            Assert.False(Directory.Exists(Path.Combine(repositoryWorkspace.Root, ".intent-cli")));
        }
        var omittedTimestamp = valid.Replace("  \"recorded_at\": \"" + timestamp + "\",\n", "", StringComparison.Ordinal);
        Assert.True(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(omittedTimestamp), false, out _, out _, out _));
        Assert.True(RulingArtifact.TryParse(validBytes, false, out _, out _, out _), "null expires_at and empty supersedes are valid controls");

        foreach (var (name, json, expectedCause, expectedDetail) in rows)
        {
            var mutated = Encoding.UTF8.GetBytes(json);
            Assert.False(RulingArtifact.TryParse(mutated, false, out _, out var parseCause, out var parseDetail), name);
            Assert.Equal(expectedCause, parseCause);
            Assert.Contains(expectedDetail, parseDetail, StringComparison.OrdinalIgnoreCase);

            foreach (var format in new[] { "json", "markdown" })
            {
                using var workspace = new TemporaryWorkspace();
                var input = workspace.Write(name + ".json", json);
                var args = new[] { "ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var result = Invoke(CreateContext(workspace.Root), args);
                Assert.Equal(1, result.ExitCode);
                using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
                Assert.Equal("refused", output.RootElement.GetProperty("disposition").GetString());
                Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
                Assert.Equal(expectedCause, output.RootElement.GetProperty("cause").GetString());
                Assert.Contains(expectedDetail, output.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
                Assert.False(output.RootElement.GetProperty("wrote").GetBoolean());
                Assert.False(output.RootElement.GetProperty("idempotent").GetBoolean());
                Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("artifact_path").ValueKind);
                Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("content_sha256").ValueKind);
                Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
            }
        }
        Assert.True(rows.Count >= 100, $"S04 matrix unexpectedly has only {rows.Count} cases.");
    }

    [Fact]
    public void SupersededRulingStaysSupersededWhenItsSuccessorExpires()
    {
        using var workspace = new TemporaryWorkspace();
        var store = new RulingArtifactStore(workspace.Root);
        var original = Artifact("R-ORIGINAL", T0, []);
        WriteArtifact(store, original);
        var originalPath = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-ORIGINAL.json");
        var originalBytes = File.ReadAllBytes(originalPath);
        var successor = Artifact("R-SUCCESSOR", T0.AddMinutes(1), ["R-ORIGINAL"]) with { ExpiresAt = T0.AddMinutes(2) };
        WriteArtifact(store, successor);

        var expiredSuccessor = store.Evaluate("intent-cli", "intent-cli-dev", successor.Id, T0.AddMinutes(2));
        Assert.Equal("expired", expiredSuccessor.Status);
        var originalAfterExpiry = store.Evaluate("intent-cli", "intent-cli-dev", original.Id, T0.AddHours(1));
        Assert.Equal("superseded", originalAfterExpiry.Status);
        Assert.Equal(new[] { "R-SUCCESSOR" }, originalAfterExpiry.ReplacementIds);
        Assert.False(store.TryWrite(original, originalBytes, T0.AddHours(1), out var rewrote, out _, out var inactiveCause, out var inactiveDetail, out _));
        Assert.False(rewrote);
        Assert.Equal("ruling-inactive-target", inactiveCause);
        Assert.Contains("terminal successor", inactiveDetail, StringComparison.Ordinal);
        Assert.Equal(originalBytes, File.ReadAllBytes(originalPath));
    }

    [Fact]
    public void CandidateAdmissionRejectsMissingSelfLateForeignAndStalePredecessors()
    {
        var cases = new[]
        {
            (Name: "missing", Seed: (RulingArtifact?)null, Child: (RulingArtifact?)null, Candidate: Artifact("R-MISSING-PRED", T0.AddMinutes(2), ["R-NOT-THERE"]), Now: T0.AddMinutes(2), Cause: "ruling-supersession-conflict", Detail: "missing predecessor"),
            (Name: "self", Seed: (RulingArtifact?)null, Child: (RulingArtifact?)null, Candidate: Artifact("R-SELF", T0.AddMinutes(2), ["R-SELF"]), Now: T0.AddMinutes(2), Cause: "ruling-supersession-conflict", Detail: "cannot supersede itself"),
            (Name: "widen-units", Seed: (RulingArtifact?)Artifact("R-PRED", T0, []), Child: (RulingArtifact?)null, Candidate: Artifact("R-WIDEN", T0.AddMinutes(1), ["R-PRED"]) with { ExecutionUnits = ["G862", "G999"] }, Now: T0.AddMinutes(1), Cause: "ruling-supersession-conflict", Detail: "different applicability"),
            (Name: "narrow-units", Seed: (RulingArtifact?)(Artifact("R-PRED", T0, []) with { ExecutionUnits = ["G862", "G999"] }), Child: (RulingArtifact?)null, Candidate: Artifact("R-NARROW", T0.AddMinutes(1), ["R-PRED"]), Now: T0.AddMinutes(1), Cause: "ruling-supersession-conflict", Detail: "different applicability"),
            (Name: "scope-kind", Seed: (RulingArtifact?)Artifact("R-PRED", T0, []), Child: (RulingArtifact?)null, Candidate: Artifact("R-KIND", T0.AddMinutes(1), ["R-PRED"]) with { ScopeKind = "repository", ExecutionUnits = [] }, Now: T0.AddMinutes(1), Cause: "ruling-supersession-conflict", Detail: "different applicability"),
            (Name: "target-repository", Seed: (RulingArtifact?)Artifact("R-PRED", T0, []), Child: (RulingArtifact?)null, Candidate: Artifact("R-REPO", T0.AddMinutes(1), ["R-PRED"]) with { TargetRepo = "other-owner/other-repo" }, Now: T0.AddMinutes(1), Cause: "ruling-supersession-conflict", Detail: "different applicability"),
            (Name: "foreign-predecessor", Seed: (RulingArtifact?)(Artifact("R-FOREIGN-PRED", T0, []) with { TargetRepo = "other-owner/other-repo" }), Child: (RulingArtifact?)null, Candidate: Artifact("R-FOREIGN-REF", T0.AddMinutes(1), ["R-FOREIGN-PRED"]), Now: T0.AddMinutes(1), Cause: "ruling-supersession-conflict", Detail: "different applicability"),
            (Name: "equal-time", Seed: (RulingArtifact?)Artifact("R-PRED", T0, []), Child: (RulingArtifact?)null, Candidate: Artifact("R-EQUAL", T0, ["R-PRED"]), Now: T0.AddMinutes(1), Cause: "ruling-supersession-conflict", Detail: "candidate is not later than"),
            (Name: "predecessor-later", Seed: (RulingArtifact?)Artifact("R-PRED", T0.AddMinutes(1), []), Child: (RulingArtifact?)null, Candidate: Artifact("R-EARLIER", T0, ["R-PRED"]), Now: T0.AddMinutes(2), Cause: "ruling-supersession-conflict", Detail: "candidate is not later than"),
            (Name: "stale-tip", Seed: (RulingArtifact?)Artifact("R-PRED", T0, []), Child: (RulingArtifact?)Artifact("R-CHILD", T0.AddMinutes(1), ["R-PRED"]), Candidate: Artifact("R-STALE", T0.AddMinutes(2), ["R-PRED"]), Now: T0.AddMinutes(2), Cause: "ruling-supersession-conflict", Detail: "every current terminal tip"),
        };

        foreach (var format in new[] { "json", "markdown" })
        foreach (var item in cases)
        using (var workspace = new TemporaryWorkspace())
        {
            if (item.Seed is not null) WriteActualRuling(workspace, workspace.Root, item.Seed, item.Seed.RecordedAt);
            var seedBytes = item.Seed is null ? null : File.ReadAllBytes(RulingPath(workspace.Root, item.Seed.Id));
            if (item.Child is not null) WriteActualRuling(workspace, workspace.Root, item.Child, item.Child.RecordedAt);
            var childBytes = item.Child is null ? null : File.ReadAllBytes(RulingPath(workspace.Root, item.Child.Id));

            var input = workspace.Write(item.Name + ".json", Encoding.UTF8.GetString(RulingArtifact.Serialize(item.Candidate)));
            var args = new[] { "ruling", "record", "--id", item.Candidate.Id, "--domain", item.Candidate.Domain, "--team", item.Candidate.Team, "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
            var result = InvokeRecordAt(CreateContext(workspace.Root), args, item.Now);
            Assert.Equal(1, result.ExitCode);
            using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
            var root = output.RootElement;
            Assert.Equal("refused", root.GetProperty("disposition").GetString());
            Assert.Equal("conflict", root.GetProperty("status").GetString());
            Assert.Equal(item.Cause, root.GetProperty("cause").GetString());
            Assert.Contains(item.Detail, root.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(root.GetProperty("wrote").GetBoolean());
            Assert.False(root.GetProperty("idempotent").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("artifact_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("content_sha256").ValueKind);
            Assert.False(File.Exists(RulingPath(workspace.Root, item.Candidate.Id)));
            if (item.Seed is not null) Assert.Equal(seedBytes, File.ReadAllBytes(RulingPath(workspace.Root, item.Seed.Id)));
            if (item.Child is not null) Assert.Equal(childBytes, File.ReadAllBytes(RulingPath(workspace.Root, item.Child.Id)));
        }

        foreach (var format in new[] { "json", "markdown" })
        using (var workspace = new TemporaryWorkspace())
        {
            var predecessor = Artifact("R-VALID-PRED", T0, []);
            WriteActualRuling(workspace, workspace.Root, predecessor, T0);
            var candidate = Artifact("R-VALID-SUCCESSOR", T0.AddMinutes(1), [predecessor.Id]);
            var input = workspace.Write("valid-successor.json", Encoding.UTF8.GetString(RulingArtifact.Serialize(candidate)));
            var args = new[] { "ruling", "record", "--id", candidate.Id, "--domain", candidate.Domain, "--team", candidate.Team, "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
            var result = InvokeRecordAt(CreateContext(workspace.Root), args, candidate.RecordedAt);
            Assert.Equal(0, result.ExitCode);
            using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
            Assert.Equal("written", output.RootElement.GetProperty("disposition").GetString());
            Assert.True(output.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Equal(RulingArtifact.Serialize(candidate), File.ReadAllBytes(RulingPath(workspace.Root, candidate.Id)));
        }

        using (var corruptWorkspace = new TemporaryWorkspace())
        {
            var store = new RulingArtifactStore(corruptWorkspace.Root);
            WriteArtifact(store, Artifact("R-BASE", T0, []));
            WriteArtifact(store, Artifact("R-CHILD", T0.AddMinutes(1), ["R-BASE"]));
            var basePath = Path.Combine(corruptWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-BASE.json");
            File.WriteAllBytes(basePath, RulingArtifact.Serialize(Artifact("R-BASE", T0, ["R-CHILD"])));
            var corrupt = store.Evaluate("intent-cli", "intent-cli-dev", "R-CHILD", T0.AddMinutes(2));
            Assert.Equal("unavailable", corrupt.Status);
            Assert.Equal("ruling-graph-conflict", corrupt.Cause);
            Assert.Contains(corrupt.Diagnostics, x => x.Path == Path.GetRelativePath(corruptWorkspace.Root, Path.GetDirectoryName(basePath)!).Replace('\\', '/') && x.Detail.Contains("R-BASE", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SymlinksDirectoriesAndFifos_AreRefusedWithoutFollowingOrBlocking()
    {
        using var workspace = new TemporaryWorkspace();
        var context = CreateContext(workspace.Root);
        var realInput = workspace.Write("real-input.json", RecordJson("R-SYMLINK-INPUT", "2026-10-10T11:00:00Z"));
        var linkedInput = Path.Combine(workspace.Root, "linked-input.json");
        File.CreateSymbolicLink(linkedInput, realInput);
        var inputResult = Invoke(context, ["ruling", "record", "--id", "R-SYMLINK-INPUT", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", linkedInput, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(1, inputResult.ExitCode);
        using (var inputJson = JsonDocument.Parse(inputResult.Output))
        {
            Assert.Equal("unavailable", inputJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("ruling-input-unavailable", inputJson.RootElement.GetProperty("cause").GetString());
            Assert.False(inputJson.RootElement.GetProperty("wrote").GetBoolean());
        }
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));

        var externalInputDirectory = workspace.CreateChild("external-inputs");
        var nestedInput = workspace.Write(Path.Combine("external-inputs", "nested.json"), RecordJson("R-ANCESTOR-SYMLINK", "2026-10-10T11:00:00Z"));
        var linkedInputDirectory = Path.Combine(workspace.Root, "linked-inputs");
        Directory.CreateSymbolicLink(linkedInputDirectory, externalInputDirectory);
        var ancestorInput = Path.Combine(linkedInputDirectory, Path.GetFileName(nestedInput));
        var ancestorResult = Invoke(context, ["ruling", "record", "--id", "R-ANCESTOR-SYMLINK", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", ancestorInput, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(1, ancestorResult.ExitCode);
        using (var ancestorJson = JsonDocument.Parse(ancestorResult.Output)) Assert.Equal("ruling-input-unavailable", ancestorJson.RootElement.GetProperty("cause").GetString());

        var directoryResult = Invoke(context, ["ruling", "record", "--id", "R-DIRECTORY-INPUT", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", externalInputDirectory, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(1, directoryResult.ExitCode);
        using (var directoryJson = JsonDocument.Parse(directoryResult.Output)) Assert.Equal("ruling-input-unavailable", directoryJson.RootElement.GetProperty("cause").GetString());
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));

        var external = workspace.CreateChild("external");
        File.WriteAllText(Path.Combine(external, "sentinel.json"), "not a ruling", new UTF8Encoding(false));
        var appRoot = Path.Combine(workspace.Root, ".intent-cli");
        Directory.CreateSymbolicLink(appRoot, external);
        var unsafeScope = new RulingArtifactStore(workspace.Root).Evaluate("intent-cli", "intent-cli-dev", "R-SYMLINK", T0);
        Assert.Equal("unavailable", unsafeScope.Status);
        Assert.Equal("ruling-unsafe-path", unsafeScope.Cause);
        Assert.Equal("not a ruling", File.ReadAllText(Path.Combine(external, "sentinel.json")));

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            using var fifoWorkspace = new TemporaryWorkspace();
            var scope = Path.Combine(fifoWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
            Directory.CreateDirectory(scope);
            var fifo = Path.Combine(scope, "R-FIFO.json");
            Assert.Equal(0, MkFifo(fifo, 0x180));
            var evaluate = Task.Run(() => new RulingArtifactStore(fifoWorkspace.Root).Evaluate("intent-cli", "intent-cli-dev", "R-FIFO", T0));
            Assert.Same(evaluate, await Task.WhenAny(evaluate, Task.Delay(TimeSpan.FromSeconds(2))));
            var fifoResult = await evaluate;
            Assert.Equal("unavailable", fifoResult.Status);
            Assert.Contains(fifoResult.Diagnostics, x => x.Path == Path.GetRelativePath(fifoWorkspace.Root, fifo).Replace('\\', '/'));

            using var fifoInputWorkspace = new TemporaryWorkspace();
            var fifoInput = Path.Combine(fifoInputWorkspace.Root, "input.fifo");
            Assert.Equal(0, MkFifo(fifoInput, 0x180));
            var fifoCommand = Invoke(CreateContext(fifoInputWorkspace.Root), ["ruling", "record", "--id", "R-FIFO-INPUT", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", fifoInput, "--authority-role", "operator", "--write", "--format", "json"]);
            Assert.Equal(1, fifoCommand.ExitCode);
            using var fifoJson = JsonDocument.Parse(fifoCommand.Output);
            Assert.Equal("ruling-input-unavailable", fifoJson.RootElement.GetProperty("cause").GetString());
            Assert.False(Directory.Exists(Path.Combine(fifoInputWorkspace.Root, ".intent-cli")));
        }
    }

    [Fact]
    public void DomainAndTeamMayEqualArtifactFileNameBecauseFinalPathIsPositional()
    {
        foreach (var (domain, team) in new[]
        {
            ("R-A.json", "intent-cli-dev"),
            ("intent-cli", "R-A.json"),
            ("R-A.json", "R-A.json"),
        })
        {
            using var workspace = new TemporaryWorkspace();
            var record = Artifact("R-A", T0, []) with { Domain = domain, Team = team };
            var input = workspace.Write("positional-target.json", Encoding.UTF8.GetString(RulingArtifact.Serialize(record)));
            var recordArgs = new[] { "ruling", "record", "--id", record.Id, "--domain", domain, "--team", team,
                "--from-file", input, "--authority-role", "operator", "--write", "--format", "json" };

            var created = InvokeRecordAt(CreateContext(workspace.Root), recordArgs, T0);
            Assert.Equal(0, created.ExitCode);
            using (var result = JsonDocument.Parse(created.Output))
            {
                Assert.Equal("written", result.RootElement.GetProperty("disposition").GetString());
                Assert.True(result.RootElement.GetProperty("wrote").GetBoolean());
            }

            var path = Path.Combine(workspace.Root, ".intent-cli", "rulings", domain, team, record.Id + ".json");
            var originalBytes = File.ReadAllBytes(path);
            Assert.Equal(RulingArtifact.Serialize(record), originalBytes);
            var replay = InvokeRecordAt(CreateContext(workspace.Root), recordArgs, T0.AddMinutes(1));
            Assert.Equal(0, replay.ExitCode);
            using (var result = JsonDocument.Parse(replay.Output))
            {
                Assert.Equal("idempotent", result.RootElement.GetProperty("disposition").GetString());
                Assert.True(result.RootElement.GetProperty("idempotent").GetBoolean());
                Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
            }

            var show = Invoke(CreateContext(workspace.Root), ["ruling", "show", record.Id, "--domain", domain, "--team", team, "--format", "json"]);
            Assert.Equal(0, show.ExitCode);
            using (var result = JsonDocument.Parse(show.Output)) Assert.Equal("active", result.RootElement.GetProperty("status").GetString());
            var validate = Invoke(CreateContext(workspace.Root), ["ruling", "validate", record.Id, "--domain", domain, "--team", team, "--format", "json"]);
            Assert.Equal(0, validate.ExitCode);
            using (var result = JsonDocument.Parse(validate.Output)) Assert.Equal("validated", result.RootElement.GetProperty("disposition").GetString());
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
        }

        using (var workspace = new TemporaryWorkspace())
        {
            var directoryAtTarget = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-DIRECTORY-TARGET.json");
            Directory.CreateDirectory(directoryAtTarget);
            var show = Invoke(CreateContext(workspace.Root), ["ruling", "show", "R-DIRECTORY-TARGET", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
            Assert.Equal(1, show.ExitCode);
            using (var result = JsonDocument.Parse(show.Output))
            {
                Assert.Equal("ruling-unsafe-path", result.RootElement.GetProperty("cause").GetString());
                Assert.Equal("unavailable", result.RootElement.GetProperty("status").GetString());
            }
        }

        using (var workspace = new TemporaryWorkspace())
        {
            var blockingFile = Path.Combine(workspace.Root, ".intent-cli");
            File.WriteAllText(blockingFile, "blocking file", new UTF8Encoding(false));
            var input = workspace.Write("blocked-intermediate.json", RecordJson("R-BLOCKED-INTERMEDIATE", "2026-10-10T12:00:00Z"));
            var record = InvokeRecordAt(CreateContext(workspace.Root), ["ruling", "record", "--id", "R-BLOCKED-INTERMEDIATE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", "json"], T0);
            Assert.Equal(1, record.ExitCode);
            using var result = JsonDocument.Parse(record.Output);
            Assert.Equal("ruling-unsafe-path", result.RootElement.GetProperty("cause").GetString());
            Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Equal("blocking file", File.ReadAllText(blockingFile));
        }
    }

    [Fact]
    public void RepositoryRootAncestorsAreCheckedBeforeRulingCommandsReadOrWrite()
    {
        using var workspace = new TemporaryWorkspace();
        var physicalRoot = workspace.CreateChild("physical-repo");
        var existing = Artifact("R-ANCESTOR", T0, []);
        var existingInput = workspace.Write("ancestor-existing.json", Encoding.UTF8.GetString(RulingArtifact.Serialize(existing)));
        var seedArgs = new[] { "ruling", "record", "--id", existing.Id, "--domain", existing.Domain, "--team", existing.Team,
            "--from-file", existingInput, "--authority-role", "operator", "--write", "--format", "json" };
        var seeded = InvokeRecordAt(CreateContext(physicalRoot), seedArgs, T0);
        Assert.Equal(0, seeded.ExitCode);
        var existingPath = Path.Combine(physicalRoot, ".intent-cli", "rulings", existing.Domain, existing.Team, existing.Id + ".json");
        var existingBytes = File.ReadAllBytes(existingPath);

        var physicalShow = Invoke(CreateContext(physicalRoot), ["ruling", "show", existing.Id, "--domain", existing.Domain, "--team", existing.Team, "--format", "json"]);
        Assert.Equal(0, physicalShow.ExitCode);
        var physicalValidate = Invoke(CreateContext(physicalRoot), ["ruling", "validate", existing.Id, "--domain", existing.Domain, "--team", existing.Team, "--format", "json"]);
        Assert.Equal(0, physicalValidate.ExitCode);

        var ancestorAlias = Path.Combine(workspace.Root, "ancestor-alias");
        Directory.CreateSymbolicLink(ancestorAlias, workspace.Root);
        var aliasedRoot = Path.Combine(ancestorAlias, "physical-repo");
        var readResult = Invoke(CreateContext(aliasedRoot), ["ruling", "show", existing.Id, "--domain", existing.Domain, "--team", existing.Team, "--format", "json"]);
        Assert.Equal(1, readResult.ExitCode);
        using (var result = JsonDocument.Parse(readResult.Output))
        {
            Assert.Equal("ruling-unsafe-path", result.RootElement.GetProperty("cause").GetString());
            Assert.Equal("unavailable", result.RootElement.GetProperty("status").GetString());
        }
        var validateResult = Invoke(CreateContext(aliasedRoot), ["ruling", "validate", existing.Id, "--domain", existing.Domain, "--team", existing.Team, "--format", "json"]);
        Assert.Equal(1, validateResult.ExitCode);
        using (var result = JsonDocument.Parse(validateResult.Output)) Assert.Equal("ruling-unsafe-path", result.RootElement.GetProperty("cause").GetString());

        var candidate = Artifact("R-ANCESTOR-CANDIDATE", T0.AddMinutes(1), []);
        var candidateInput = workspace.Write("ancestor-candidate.json", Encoding.UTF8.GetString(RulingArtifact.Serialize(candidate)));
        var candidateArgs = new[] { "ruling", "record", "--id", candidate.Id, "--domain", candidate.Domain, "--team", candidate.Team,
            "--from-file", candidateInput, "--authority-role", "operator", "--write", "--format", "json" };
        var contentReads = 0;
        using var writer = new StringWriter();
        var candidateExit = RulingCommand.ExecuteRecord(CreateContext(aliasedRoot), candidateArgs[2..], writer,
            (operation, _) => { if (operation == "read") contentReads++; }, T0.AddMinutes(1));
        Assert.Equal(0, contentReads);
        Assert.Equal(1, candidateExit);
        using (var result = JsonDocument.Parse(writer.ToString()))
        {
            Assert.Equal("ruling-unsafe-path", result.RootElement.GetProperty("cause").GetString());
            Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
        }
        var candidatePath = Path.Combine(physicalRoot, ".intent-cli", "rulings", candidate.Domain, candidate.Team, candidate.Id + ".json");
        Assert.False(File.Exists(candidatePath));
        Assert.Equal(existingBytes, File.ReadAllBytes(existingPath));

        var directRootAlias = Path.Combine(workspace.Root, "repo-root-alias");
        Directory.CreateSymbolicLink(directRootAlias, physicalRoot);
        var directAliasResult = Invoke(CreateContext(directRootAlias), ["ruling", "show", existing.Id, "--domain", existing.Domain, "--team", existing.Team, "--format", "json"]);
        Assert.Equal(1, directAliasResult.ExitCode);
        using (var result = JsonDocument.Parse(directAliasResult.Output)) Assert.Equal("ruling-unsafe-path", result.RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void ActualLocalWriter_RequiresExternalBareRemotePushForFreshCloneVisibility()
    {
        using var workspace = new TemporaryWorkspace();
        var bare = workspace.CreateChild("origin.git");
        var writerClone = Path.Combine(workspace.Root, "writer-clone");
        var beforeClone = Path.Combine(workspace.Root, "before-publication");
        var afterClone = Path.Combine(workspace.Root, "after-publication");
        RunGit(workspace.Root, "init", "--bare", "--initial-branch=main", bare);
        RunGit(workspace.Root, "clone", bare, writerClone);
        RunGit(writerClone, "config", "user.name", "G862 test operator");
        RunGit(writerClone, "config", "user.email", "g862-test@example.invalid");
        File.WriteAllText(Path.Combine(writerClone, "README.md"), "baseline\n", new UTF8Encoding(false));
        RunGit(writerClone, "add", "README.md");
        RunGit(writerClone, "commit", "-m", "baseline");
        RunGit(writerClone, "push", "origin", "HEAD:main");
        RunGit(workspace.Root, "clone", bare, beforeClone);
        var baselineHead = RunGit(writerClone, "rev-parse", "HEAD").Trim();
        var baselineRemoteHead = RunGit(writerClone, "ls-remote", "origin", "refs/heads/main").Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        var supplied = workspace.Write("operator-ruling.json", RecordJson("R-REMOTE", "2026-10-10T11:00:00Z"));
        var written = Invoke(CreateContext(writerClone), ["ruling", "record", "--id", "R-REMOTE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", supplied, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(0, written.ExitCode);
        using var writeJson = JsonDocument.Parse(written.Output);
        Assert.Equal("not-verified", writeJson.RootElement.GetProperty("publication_status").GetString());
        Assert.True(writeJson.RootElement.GetProperty("wrote").GetBoolean());
        Assert.Equal(baselineHead, RunGit(writerClone, "rev-parse", "HEAD").Trim());
        Assert.Equal(baselineRemoteHead, RunGit(writerClone, "ls-remote", "origin", "refs/heads/main").Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        Assert.Equal("?? .intent-cli/\n", RunGit(writerClone, "status", "--short"));

        var beforeShow = Invoke(CreateContext(beforeClone), ["ruling", "show", "R-REMOTE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
        Assert.Equal(1, beforeShow.ExitCode);
        using (var missing = JsonDocument.Parse(beforeShow.Output)) Assert.Equal("missing", missing.RootElement.GetProperty("status").GetString());

        var artifactRelative = Path.Combine(".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-REMOTE.json");
        RunGit(writerClone, "add", "--", artifactRelative);
        RunGit(writerClone, "commit", "-m", "publish supplied ruling artifact");
        RunGit(writerClone, "push", "origin", "HEAD:main");
        RunGit(workspace.Root, "clone", bare, afterClone);
        var freshShow = Invoke(CreateContext(afterClone), ["ruling", "show", "R-REMOTE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
        Assert.Equal(0, freshShow.ExitCode);
        using var showJson = JsonDocument.Parse(freshShow.Output);
        Assert.Equal("active", showJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("not-verified", showJson.RootElement.GetProperty("publication_status").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(afterClone, artifactRelative)))).ToLowerInvariant(), showJson.RootElement.GetProperty("content_sha256").GetString());
        var freshValidate = Invoke(CreateContext(afterClone), ["ruling", "validate", "R-REMOTE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
        Assert.Equal(0, freshValidate.ExitCode);
        using var validateJson = JsonDocument.Parse(freshValidate.Output);
        Assert.Equal("validated", validateJson.RootElement.GetProperty("disposition").GetString());
    }

    [Fact]
    public void GraphAdmission_RequiresAllCurrentTipsAndReportsDescendantTerminal()
    {
        using var workspace = new TemporaryWorkspace();
        var store = new RulingArtifactStore(workspace.Root);
        WriteArtifact(store, Artifact("R-A", T0, []));
        var left = workspace.CreateChild("left");
        var right = workspace.CreateChild("right");
        CopyScope(workspace.Root, left);
        CopyScope(workspace.Root, right);
        WriteArtifact(new RulingArtifactStore(left), Artifact("R-B", T0.AddMinutes(1), ["R-A"]));
        WriteArtifact(new RulingArtifactStore(right), Artifact("R-C", T0.AddMinutes(2), ["R-A"]));
        CopyArtifact(left, workspace.Root, "R-B");
        CopyArtifact(right, workspace.Root, "R-C");

        var a = store.Evaluate("intent-cli", "intent-cli-dev", "R-A", T0.AddMinutes(3));
        Assert.Equal("conflict", a.Status);
        Assert.Equal("ruling-successor-conflict", a.Cause);
        Assert.Equal(new[] { "R-B", "R-C" }, a.SupersededBy);
        Assert.Equal(new[] { "R-B", "R-C" }, a.ReplacementIds);
        var partial = store.Evaluate("intent-cli", "intent-cli-dev", "R-D", T0.AddMinutes(4), Artifact("R-D", T0.AddMinutes(4), ["R-B"]), true);
        Assert.Equal("conflict", partial.Status);
        Assert.Equal("ruling-incomplete-merge", partial.Cause);
        var partialPath = workspace.Write("partial-merge.json", Encoding.UTF8.GetString(RulingArtifact.Serialize(Artifact("R-D", T0.AddMinutes(4), ["R-B"]))));
        var partialCommand = Invoke(CreateContext(workspace.Root), ["ruling", "record", "--id", "R-D", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", partialPath, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(1, partialCommand.ExitCode);
        using (var partialOutput = JsonDocument.Parse(partialCommand.Output))
        {
            Assert.Equal("refused", partialOutput.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("conflict", partialOutput.RootElement.GetProperty("status").GetString());
            Assert.Equal("ruling-incomplete-merge", partialOutput.RootElement.GetProperty("cause").GetString());
            Assert.False(partialOutput.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Equal("R-D", partialOutput.RootElement.GetProperty("normalized_record").GetProperty("id").GetString());
        }
        Assert.False(File.Exists(Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), "R-D.json")));
        var repaired = Artifact("R-D", T0.AddMinutes(4), ["R-B", "R-C"]);
        var joined = store.Evaluate("intent-cli", "intent-cli-dev", "R-D", T0.AddMinutes(4), repaired, true);
        Assert.Equal("active", joined.Status);
        Assert.True(store.TryWrite(repaired, RulingArtifact.Serialize(repaired), T0.AddMinutes(4), out var wrote, out _, out var cause, out var detail, out _), $"{cause}: {detail}");
        Assert.True(wrote);
        var afterJoin = store.Evaluate("intent-cli", "intent-cli-dev", "R-A", T0.AddMinutes(5));
        Assert.Equal("superseded", afterJoin.Status);
        Assert.Equal(new[] { "R-B", "R-C" }, afterJoin.SupersededBy);
        Assert.Equal(new[] { "R-D" }, afterJoin.ReplacementIds);
    }

    [Fact]
    public void ConflictReadCommandsRetainVerifiedTargetAndIndependentExpiryInJsonAndMarkdown()
    {
        static (TemporaryWorkspace Workspace, string Path) Fork(bool expired)
        {
            var workspace = new TemporaryWorkspace();
            var now = DateTimeOffset.UtcNow;
            var recorded = now.AddHours(-3);
            var target = Artifact(expired ? "R-EXPIRED-FORK" : "R-LIVE-FORK", recorded, []) with
            { ExpiresAt = expired ? now.AddHours(-2) : now.AddHours(1) };
            var store = new RulingArtifactStore(workspace.Root);
            WriteArtifact(store, target);
            var left = workspace.CreateChild("fork-left");
            var right = workspace.CreateChild("fork-right");
            CopyScope(workspace.Root, left);
            CopyScope(workspace.Root, right);
            var childAt = now.AddHours(-1);
            var b = Artifact(target.Id + "-B", childAt, [target.Id]);
            var c = Artifact(target.Id + "-C", childAt.AddMinutes(1), [target.Id]);
            WriteArtifact(new RulingArtifactStore(left), b);
            WriteArtifact(new RulingArtifactStore(right), c);
            CopyArtifact(left, workspace.Root, b.Id);
            CopyArtifact(right, workspace.Root, c.Id);
            return (workspace, Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", target.Id + ".json"));
        }

        foreach (var expired in new[] { true, false })
        {
            var (workspace, targetPath) = Fork(expired);
            using (workspace)
            {
                var id = Path.GetFileNameWithoutExtension(targetPath);
                var context = CreateContext(workspace.Root);
                var input = workspace.Write("replay.json", File.ReadAllText(targetPath));
                var commands = new (string[] Args, string Format)[]
                {
                    (["ruling", "show", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], "json"),
                    (["ruling", "validate", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "markdown"], "markdown"),
                    (["ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--format", "json"], "json"),
                    (["ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--format", "markdown"], "markdown"),
                };
                foreach (var (args, format) in commands)
                {
                    var before = File.ReadAllBytes(targetPath);
                    var output = Invoke(context, args);
                    Assert.Equal(1, output.ExitCode);
                    using var json = format == "json" ? JsonDocument.Parse(output.Output) : StructuredJsonFromMarkdown(output.Output);
                    var root = json.RootElement;
                    Assert.Equal("conflict", root.GetProperty("status").GetString());
                    Assert.Equal("ruling-successor-conflict", root.GetProperty("cause").GetString());
                    Assert.Equal(expired, root.GetProperty("expired").GetBoolean());
                    Assert.Equal(id, root.GetProperty("normalized_record").GetProperty("id").GetString());
                    Assert.Equal(Convert.ToHexString(SHA256.HashData(before)).ToLowerInvariant(), root.GetProperty("content_sha256").GetString());
                    Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/" + id + ".json", root.GetProperty("artifact_path").GetString());
                    if (args[1] == "record") Assert.Equal("existing", root.GetProperty("timestamp_source").GetString());
                    else Assert.Equal(JsonValueKind.Null, root.GetProperty("timestamp_source").ValueKind);
                    Assert.Equal(before, File.ReadAllBytes(targetPath));
                }
            }
        }

        using var missing = new TemporaryWorkspace();
        var absent = Invoke(CreateContext(missing.Root), ["ruling", "show", "R-MISSING", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
        using var absentJson = JsonDocument.Parse(absent.Output);
        Assert.Equal(JsonValueKind.Null, absentJson.RootElement.GetProperty("expired").ValueKind);
    }

    [Fact]
    public void IdentityAliasAndEmbeddedMismatchAreRefusedAsConflicts()
    {
        using (var aliasWorkspace = new TemporaryWorkspace())
        {
            var alias = Path.Combine(aliasWorkspace.Root, ".intent-cli", "rulings", "Intent-CLI", "intent-cli-dev");
            Directory.CreateDirectory(alias);
            var collision = new RulingArtifactStore(aliasWorkspace.Root).Evaluate("intent-cli", "intent-cli-dev", "R-ALIAS", T0);
            Assert.Equal("conflict", collision.Status);
            Assert.Equal("ruling-identity-conflict", collision.Cause);
        }

        foreach (var mismatch in new[] { "id", "domain", "team" })
        using (var workspace = new TemporaryWorkspace())
        {
            var scope = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
            Directory.CreateDirectory(scope);
            var mismatched = Artifact("R-EMBEDDED", T0, []) with
            {
                Id = mismatch == "id" ? "R-OTHER" : "R-EMBEDDED",
                Domain = mismatch == "domain" ? "other-domain" : "intent-cli",
                Team = mismatch == "team" ? "other-team" : "intent-cli-dev",
            };
            var artifactPath = Path.Combine(scope, "R-EMBEDDED.json");
            File.WriteAllBytes(artifactPath, RulingArtifact.Serialize(mismatched));
            var before = File.ReadAllBytes(artifactPath);

            foreach (var format in new[] { "json", "markdown" })
            foreach (var operation in new[] { "show", "validate" })
            {
                var result = Invoke(CreateContext(workspace.Root), ["ruling", operation, "R-EMBEDDED", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format]);
                Assert.Equal(1, result.ExitCode);
                using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
                var root = output.RootElement;
                Assert.Equal("refused", root.GetProperty("disposition").GetString());
                Assert.Equal("conflict", root.GetProperty("status").GetString());
                Assert.Equal("ruling-identity-conflict", root.GetProperty("cause").GetString());
                Assert.Contains("filename and embedded domain/team/id disagree", root.GetProperty("detail").GetString(), StringComparison.Ordinal);
                var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-EMBEDDED.json", diagnostic.GetProperty("path").GetString());
                Assert.Equal("ruling-identity-conflict", diagnostic.GetProperty("cause").GetString());
                Assert.Equal(before, File.ReadAllBytes(artifactPath));
            }
        }

        using var inputWorkspace = new TemporaryWorkspace();
        var input = inputWorkspace.Write("wrong-identity.json", RecordJson("R-OTHER", "2026-10-10T12:00:00Z"));
        var command = Invoke(CreateContext(inputWorkspace.Root), ["ruling", "record", "--id", "R-EXPECTED", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", "json"]);
        using var commandOutput = JsonDocument.Parse(command.Output);
        Assert.Equal("refused", commandOutput.RootElement.GetProperty("disposition").GetString());
        Assert.Equal("conflict", commandOutput.RootElement.GetProperty("status").GetString());
        Assert.Equal("ruling-identity-conflict", commandOutput.RootElement.GetProperty("cause").GetString());
    }

    [Fact]
    public void SupersededAndExpiredReadCommandsRetainDiskEvidenceAndDoNotChangeBytes()
    {
        static void AssertNormalizedMatches(JsonElement output, byte[] expected)
        {
            Assert.True(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(output.GetRawText()), false, out var normalized, out var cause, out var detail), $"{cause}: {detail}");
            Assert.Equal(expected, RulingArtifact.Serialize(normalized!));
        }

        foreach (var format in new[] { "json", "markdown" })
        using (var workspace = new TemporaryWorkspace())
        {
            var context = CreateContext(workspace.Root);
            var originalPath = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-S13-A.json");
            var aInput = workspace.Write("a.json", RecordJson("R-S13-A", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
            var aArgs = new[] { "ruling", "record", "--id", "R-S13-A", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", aInput, "--authority-role", "operator", "--write", "--format", "json" };
            Assert.Equal(0, InvokeRecordAt(context, aArgs, T0).ExitCode);
            var aBytes = File.ReadAllBytes(originalPath);
            var aHash = Convert.ToHexString(SHA256.HashData(aBytes)).ToLowerInvariant();

            var bInput = workspace.Write("b.json", RecordJson("R-S13-B", T0.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
                .Replace("\"supersedes\": []", "\"supersedes\": [\"R-S13-A\"]", StringComparison.Ordinal));
            var bArgs = new[] { "ruling", "record", "--id", "R-S13-B", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", bInput, "--authority-role", "operator", "--write", "--format", "json" };
            Assert.Equal(0, InvokeRecordAt(context, bArgs, T0.AddMinutes(1)).ExitCode);

            var expiry = T0.AddSeconds(10);
            var eInput = workspace.Write("e.json", RecordJson("R-S13-E", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
                .Replace("\"expires_at\": null", $"\"expires_at\": \"{expiry:yyyy-MM-dd'T'HH:mm:ss'Z'}\"", StringComparison.Ordinal));
            var eArgs = new[] { "ruling", "record", "--id", "R-S13-E", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", eInput, "--authority-role", "operator", "--write", "--format", "json" };
            Assert.Equal(0, InvokeRecordAt(context, eArgs, T0).ExitCode);
            var ePath = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-S13-E.json");
            var eBytes = File.ReadAllBytes(ePath);
            var eHash = Convert.ToHexString(SHA256.HashData(eBytes)).ToLowerInvariant();

            var superseded = InvokeReadAt(context, "validate", ["ruling", "validate", "R-S13-A", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], T0.AddMinutes(2));
            Assert.Equal(1, superseded.ExitCode);
            using (var output = format == "json" ? JsonDocument.Parse(superseded.Output) : StructuredJsonFromMarkdown(superseded.Output))
            {
                var root = output.RootElement;
                Assert.Equal("refused", root.GetProperty("disposition").GetString());
                Assert.Equal("superseded", root.GetProperty("status").GetString());
                Assert.Equal("R-S13-A", root.GetProperty("normalized_record").GetProperty("id").GetString());
                Assert.Equal(aHash, root.GetProperty("content_sha256").GetString());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-S13-A.json", root.GetProperty("artifact_path").GetString());
                Assert.False(root.GetProperty("expired").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("timestamp_source").ValueKind);
                AssertNormalizedMatches(root.GetProperty("normalized_record"), aBytes);
            }
            var expiredValidate = InvokeReadAt(context, "validate", ["ruling", "validate", "R-S13-E", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], expiry);
            Assert.Equal(1, expiredValidate.ExitCode);
            using (var output = format == "json" ? JsonDocument.Parse(expiredValidate.Output) : StructuredJsonFromMarkdown(expiredValidate.Output))
            {
                var root = output.RootElement;
                Assert.Equal("refused", root.GetProperty("disposition").GetString());
                Assert.Equal("expired", root.GetProperty("status").GetString());
                Assert.Equal("R-S13-E", root.GetProperty("normalized_record").GetProperty("id").GetString());
                Assert.Equal(eHash, root.GetProperty("content_sha256").GetString());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-S13-E.json", root.GetProperty("artifact_path").GetString());
                Assert.True(root.GetProperty("expired").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("timestamp_source").ValueKind);
                AssertNormalizedMatches(root.GetProperty("normalized_record"), eBytes);
            }
            var expiredShow = InvokeReadAt(context, "show", ["ruling", "show", "R-S13-E", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], expiry);
            Assert.Equal(0, expiredShow.ExitCode);
            using (var output = format == "json" ? JsonDocument.Parse(expiredShow.Output) : StructuredJsonFromMarkdown(expiredShow.Output))
            {
                var root = output.RootElement;
                Assert.Equal("shown", root.GetProperty("disposition").GetString());
                Assert.Equal("expired", root.GetProperty("status").GetString());
                Assert.Equal(eHash, root.GetProperty("content_sha256").GetString());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-S13-E.json", root.GetProperty("artifact_path").GetString());
                Assert.True(root.GetProperty("expired").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("timestamp_source").ValueKind);
                AssertNormalizedMatches(root.GetProperty("normalized_record"), eBytes);
            }

            var replayArgs = aArgs.ToArray();
            replayArgs[^1] = format;
            var replay = InvokeRecordAt(context, replayArgs, T0.AddMinutes(2));
            Assert.Equal(1, replay.ExitCode);
            using (var output = format == "json" ? JsonDocument.Parse(replay.Output) : StructuredJsonFromMarkdown(replay.Output))
            {
                var root = output.RootElement;
                Assert.Equal("refused", root.GetProperty("disposition").GetString());
                Assert.Equal("superseded", root.GetProperty("status").GetString());
                Assert.Equal("ruling-inactive-target", root.GetProperty("cause").GetString());
                Assert.False(root.GetProperty("wrote").GetBoolean());
                Assert.False(root.GetProperty("idempotent").GetBoolean());
                Assert.Equal(aHash, root.GetProperty("content_sha256").GetString());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-S13-A.json", root.GetProperty("artifact_path").GetString());
                AssertNormalizedMatches(root.GetProperty("normalized_record"), aBytes);
            }
            var expiredReplayArgs = eArgs.ToArray();
            expiredReplayArgs[^1] = format;
            var expiredReplay = InvokeRecordAt(context, expiredReplayArgs, expiry);
            Assert.Equal(1, expiredReplay.ExitCode);
            using (var output = format == "json" ? JsonDocument.Parse(expiredReplay.Output) : StructuredJsonFromMarkdown(expiredReplay.Output))
            {
                var root = output.RootElement;
                Assert.Equal("refused", root.GetProperty("disposition").GetString());
                Assert.Equal("expired", root.GetProperty("status").GetString());
                Assert.Equal("ruling-inactive-target", root.GetProperty("cause").GetString());
                Assert.True(root.GetProperty("expired").GetBoolean());
                Assert.False(root.GetProperty("wrote").GetBoolean());
                Assert.False(root.GetProperty("idempotent").GetBoolean());
                Assert.Equal(eHash, root.GetProperty("content_sha256").GetString());
                Assert.Equal(ePath.Replace(workspace.Root + Path.DirectorySeparatorChar, "", StringComparison.Ordinal).Replace('\\', '/'), root.GetProperty("artifact_path").GetString());
                AssertNormalizedMatches(root.GetProperty("normalized_record"), eBytes);
            }
            Assert.Equal(aBytes, File.ReadAllBytes(originalPath));
            Assert.Equal(eBytes, File.ReadAllBytes(ePath));
        }
    }

    [Fact]
    public void ExpiredNewCandidateIsRefusedWithKnownExpiryButNoDiskEvidence()
    {
        foreach (var format in new[] { "json", "markdown" })
        foreach (var expiresAt in new[] { T0, T0.AddSeconds(-1) })
        foreach (var write in new[] { false, true })
        using (var workspace = new TemporaryWorkspace())
        {
            var id = $"R-EXPIRED-CANDIDATE-{expiresAt.Ticks}-{write}-{format}";
            var recordedAt = expiresAt.AddMinutes(-1);
            var json = RecordJson(id, recordedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("\"expires_at\": null", $"\"expires_at\": \"{expiresAt:yyyy-MM-dd'T'HH:mm:ss'Z'}\"", StringComparison.Ordinal);
            var input = workspace.Write("expired-candidate.json", json);
            var args = new List<string> { "ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator" };
            if (write) args.Add("--write");
            args.AddRange(["--format", format]);

            var result = InvokeRecordAt(CreateContext(workspace.Root), args.ToArray(), expiresAt);
            Assert.Equal(1, result.ExitCode);
            using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
            var root = output.RootElement;
            Assert.Equal("refused", root.GetProperty("disposition").GetString());
            Assert.Equal("expired", root.GetProperty("status").GetString());
            Assert.Equal("ruling-expired-candidate", root.GetProperty("cause").GetString());
            Assert.True(root.GetProperty("expired").GetBoolean());
            Assert.False(root.GetProperty("wrote").GetBoolean());
            Assert.False(root.GetProperty("idempotent").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("artifact_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("content_sha256").ValueKind);
            Assert.Equal(id, root.GetProperty("normalized_record").GetProperty("id").GetString());
            Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
        }

        foreach (var format in new[] { "json", "markdown" })
        foreach (var write in new[] { false, true })
        using (var workspace = new TemporaryWorkspace())
        {
            const string id = "R-EXPIRY-ONE-TICK-BEFORE";
            var expiry = T0.AddSeconds(1);
            var recordedAt = T0.AddMinutes(-1);
            var json = RecordJson(id, recordedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("\"expires_at\": null", $"\"expires_at\": \"{expiry:yyyy-MM-dd'T'HH:mm:ss'Z'}\"", StringComparison.Ordinal);
            var input = workspace.Write("near-expiry.json", json);
            var args = new List<string> { "ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator" };
            if (write) args.Add("--write");
            args.AddRange(["--format", format]);
            var now = expiry.AddTicks(-1);
            var result = InvokeRecordAt(CreateContext(workspace.Root), args.ToArray(), now);
            Assert.Equal(0, result.ExitCode);
            using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
            Assert.Equal(write ? "written" : "preview", output.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("active", output.RootElement.GetProperty("status").GetString());
            if (write) Assert.False(output.RootElement.GetProperty("expired").GetBoolean());
            else Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("expired").ValueKind);
            Assert.Equal(write, output.RootElement.GetProperty("wrote").GetBoolean());
            if (write)
            {
                Assert.Equal(RulingArtifact.Serialize(Artifact(id, recordedAt, [] ) with { ExpiresAt = expiry }), File.ReadAllBytes(RulingPath(workspace.Root, id)));
            }
            else Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
        }
    }

    [Fact]
    public void ActualIdentifierFlagsRejectUnsafeValuesAndAcceptAll128CharacterFields()
    {
        var invalidValues = new[] { "has/slash", "has\\\\slash", ".hidden", "..", " leading", "trailing ", new string('A', 129) };
        foreach (var (field, cause) in new[] { ("id", "ruling-invalid-id"), ("domain", "ruling-invalid-domain"), ("team", "ruling-invalid-team") })
        foreach (var value in invalidValues)
        foreach (var format in new[] { "json", "markdown" })
        foreach (var operation in new[] { "record", "show" })
        using (var workspace = new TemporaryWorkspace())
        {
            var input = workspace.Write("valid-input.json", RecordJson("R-VALID-IDENTITY", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)));
            var inputBytes = File.ReadAllBytes(input);
            var sentinel = workspace.Write("foreign-sentinel.txt", "untouched identity sentinel");
            var sentinelBytes = File.ReadAllBytes(sentinel);
            var id = field == "id" ? value : "R-VALID-IDENTITY";
            var domain = field == "domain" ? value : "intent-cli";
            var team = field == "team" ? value : "intent-cli-dev";
            string[] args = operation == "record"
                ? ["ruling", "record", "--id", id, "--domain", domain, "--team", team, "--from-file", input, "--authority-role", "operator", "--write", "--format", format]
                : ["ruling", "show", id, "--domain", domain, "--team", team, "--format", format];
            var result = Invoke(CreateContext(workspace.Root), args);
            Assert.Equal(1, result.ExitCode);
            using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
            var root = output.RootElement;
            Assert.Equal("refused", root.GetProperty("disposition").GetString());
            Assert.Equal("unavailable", root.GetProperty("status").GetString());
            Assert.Equal(cause, root.GetProperty("cause").GetString());
            Assert.False(root.GetProperty("wrote").GetBoolean());
            Assert.False(root.GetProperty("idempotent").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("artifact_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("content_sha256").ValueKind);
            Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
            Assert.Equal(inputBytes, File.ReadAllBytes(input));
            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinel));
        }

        var longId = new string('I', 128);
        var longDomain = new string('D', 128);
        var longTeam = new string('T', 128);
        Assert.Equal(128, longId.Length);
        Assert.Equal(128, longDomain.Length);
        Assert.Equal(128, longTeam.Length);
        foreach (var format in new[] { "json", "markdown" })
        using (var workspace = new TemporaryWorkspace())
        {
            var json = RecordJson(longId, T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("\"domain\": \"intent-cli\"", $"\"domain\": \"{longDomain}\"", StringComparison.Ordinal)
                .Replace("\"team\": \"intent-cli-dev\"", $"\"team\": \"{longTeam}\"", StringComparison.Ordinal);
            var input = workspace.Write("long-identifiers.json", json);
            var context = CreateContext(workspace.Root);
            var write = InvokeRecordAt(context, ["ruling", "record", "--id", longId, "--domain", longDomain, "--team", longTeam, "--from-file", input, "--authority-role", "operator", "--write", "--format", format], T0);
            Assert.Equal(0, write.ExitCode);
            using (var output = format == "json" ? JsonDocument.Parse(write.Output) : StructuredJsonFromMarkdown(write.Output))
            {
                Assert.Equal("written", output.RootElement.GetProperty("disposition").GetString());
                Assert.True(output.RootElement.GetProperty("wrote").GetBoolean());
            }
            var path = Path.Combine(workspace.Root, ".intent-cli", "rulings", longDomain, longTeam, longId + ".json");
            var bytes = File.ReadAllBytes(path);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            foreach (var operation in new[] { "show", "validate" })
            {
                var args = new[] { "ruling", operation, longId, "--domain", longDomain, "--team", longTeam, "--format", format };
                var read = InvokeReadAt(context, operation, args, T0);
                Assert.Equal(0, read.ExitCode);
                using var output = format == "json" ? JsonDocument.Parse(read.Output) : StructuredJsonFromMarkdown(read.Output);
                Assert.Equal(hash, output.RootElement.GetProperty("content_sha256").GetString());
                Assert.Equal(".intent-cli/rulings/" + longDomain + "/" + longTeam + "/" + longId + ".json", output.RootElement.GetProperty("artifact_path").GetString());
            }
        }
    }

    [Fact]
    public void NestedScopeDirectoryIsInvalidLayoutForReadsAndAdmission()
    {
        foreach (var format in new[] { "json", "markdown" })
        using (var workspace = new TemporaryWorkspace())
        {
            var context = CreateContext(workspace.Root);
            var existing = Artifact("R-NESTED-LAYOUT-EXISTING", T0, []);
            WriteActualRuling(workspace, workspace.Root, existing, T0);
            var existingPath = RulingPath(workspace.Root, existing.Id);
            var existingBytes = File.ReadAllBytes(existingPath);
            var nestedDirectory = Path.Combine(Path.GetDirectoryName(existingPath)!, "unrelated-nested-directory");
            Directory.CreateDirectory(nestedDirectory);
            var nestedSentinel = Path.Combine(nestedDirectory, "R-UNRELATED.json");
            File.WriteAllText(nestedSentinel, "do not inspect nested payload", new UTF8Encoding(false));
            var nestedBytes = File.ReadAllBytes(nestedSentinel);

            foreach (var operation in new[] { "show", "validate" })
            {
                var read = InvokeReadAt(context, operation, ["ruling", operation, existing.Id, "--domain", existing.Domain, "--team", existing.Team, "--format", format], T0.AddMinutes(1));
                Assert.Equal(1, read.ExitCode);
                using var output = format == "json" ? JsonDocument.Parse(read.Output) : StructuredJsonFromMarkdown(read.Output);
                Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
                Assert.Equal("ruling-invalid-layout", output.RootElement.GetProperty("cause").GetString());
            }

            var candidate = Artifact("R-NESTED-LAYOUT-CANDIDATE", T0.AddMinutes(1), []);
            var input = workspace.Write("nested-layout-candidate.json", Encoding.UTF8.GetString(RulingArtifact.Serialize(candidate)));
            var args = new[] { "ruling", "record", "--id", candidate.Id, "--domain", candidate.Domain, "--team", candidate.Team,
                "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
            var visitedNestedPayload = false;
            using var writer = new StringWriter();
            var exit = RulingCommand.ExecuteRecord(context, args[2..], writer, (operation, path) =>
            {
                if (operation == "read" && Path.GetFullPath(path).StartsWith(Path.GetFullPath(nestedDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    visitedNestedPayload = true;
            }, T0.AddMinutes(1));
            Assert.Equal(1, exit);
            using var recordOutput = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString());
            Assert.Equal("unavailable", recordOutput.RootElement.GetProperty("status").GetString());
            Assert.Equal("ruling-invalid-layout", recordOutput.RootElement.GetProperty("cause").GetString());
            Assert.False(recordOutput.RootElement.GetProperty("wrote").GetBoolean());
            Assert.False(visitedNestedPayload);
            Assert.False(File.Exists(RulingPath(workspace.Root, candidate.Id)));
            Assert.Equal(existingBytes, File.ReadAllBytes(existingPath));
            Assert.Equal(nestedBytes, File.ReadAllBytes(nestedSentinel));
        }
    }

    [Fact]
    public void OmittedRecordedAtRejectsAlreadyExpiredTimestampAndAcceptsOneTickLater()
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using (var expiredWorkspace = new TemporaryWorkspace())
            {
                var expiredJson = RecordJson("R-OMITTED-EXPIRED", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("  \"recorded_at\": \"2026-10-10T12:00:00Z\",\n", "", StringComparison.Ordinal)
                    .Replace("\"expires_at\": null", "\"expires_at\": \"2026-10-10T12:00:00Z\"", StringComparison.Ordinal);
                Assert.True(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(expiredJson), false, out _, out _, out _));
                var input = expiredWorkspace.Write("omitted-expired.json", expiredJson);
                var args = new[] { "ruling", "record", "--id", "R-OMITTED-EXPIRED", "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var result = InvokeRecordAt(CreateContext(expiredWorkspace.Root), args, T0);
                Assert.Equal(1, result.ExitCode);
                using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
                Assert.Equal("refused", output.RootElement.GetProperty("disposition").GetString());
                Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
                Assert.Equal("ruling-invalid-timestamp", output.RootElement.GetProperty("cause").GetString());
                Assert.False(output.RootElement.GetProperty("wrote").GetBoolean());
                Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("artifact_path").ValueKind);
                Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("content_sha256").ValueKind);
                Assert.False(Directory.Exists(Path.Combine(expiredWorkspace.Root, ".intent-cli")));
            }

            using (var validWorkspace = new TemporaryWorkspace())
            {
                var validJson = RecordJson("R-OMITTED-VALID", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("  \"recorded_at\": \"2026-10-10T12:00:00Z\",\n", "", StringComparison.Ordinal)
                    .Replace("\"expires_at\": null", "\"expires_at\": \"2026-10-10T12:00:00.0000001Z\"", StringComparison.Ordinal);
                Assert.True(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(validJson), false, out _, out _, out _));
                var input = validWorkspace.Write("omitted-valid.json", validJson);
                var args = new[] { "ruling", "record", "--id", "R-OMITTED-VALID", "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var result = InvokeRecordAt(CreateContext(validWorkspace.Root), args, T0);
                Assert.Equal(0, result.ExitCode);
                using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
                Assert.Equal("written", output.RootElement.GetProperty("disposition").GetString());
                Assert.True(output.RootElement.GetProperty("wrote").GetBoolean());
                Assert.Equal(RulingArtifact.Serialize(Artifact("R-OMITTED-VALID", T0, []) with { ExpiresAt = T0.AddTicks(1) }),
                    File.ReadAllBytes(RulingPath(validWorkspace.Root, "R-OMITTED-VALID")));
            }
        }
    }

    [Fact]
    public void SameIdInForeignScopesNeverBecomesCurrentScopeEvidence()
    {
        foreach (var foreign in new[] { (Domain: "other-domain", Team: "intent-cli-dev"), (Domain: "intent-cli", Team: "other-team") })
        using (var workspace = new TemporaryWorkspace())
        {
            const string id = "R-S03-SAME-ID";
            var context = CreateContext(workspace.Root);
            var json = RecordJson(id, T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
                .Replace("\"domain\": \"intent-cli\"", $"\"domain\": \"{foreign.Domain}\"", StringComparison.Ordinal)
                .Replace("\"team\": \"intent-cli-dev\"", $"\"team\": \"{foreign.Team}\"", StringComparison.Ordinal);
            var input = workspace.Write("foreign.json", json);
            var args = new[] { "ruling", "record", "--id", id, "--domain", foreign.Domain, "--team", foreign.Team, "--from-file", input, "--authority-role", "operator", "--write", "--format", "json" };
            Assert.Equal(0, InvokeRecordAt(context, args, T0).ExitCode);
            var foreignPath = Path.Combine(workspace.Root, ".intent-cli", "rulings", foreign.Domain, foreign.Team, id + ".json");
            var bytes = File.ReadAllBytes(foreignPath);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            foreach (var format in new[] { "json", "markdown" })
            foreach (var operation in new[] { "show", "validate" })
            {
                var read = InvokeReadAt(context, operation, ["ruling", operation, id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format], T0.AddMinutes(1));
                Assert.Equal(1, read.ExitCode);
                using var output = format == "json" ? JsonDocument.Parse(read.Output) : StructuredJsonFromMarkdown(read.Output);
                Assert.Equal("missing", output.RootElement.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("artifact_path").ValueKind);
                Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("content_sha256").ValueKind);
                Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("normalized_record").ValueKind);
            }
            Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(foreignPath))).ToLowerInvariant());
            Assert.Equal(bytes, File.ReadAllBytes(foreignPath));
            Assert.False(File.Exists(Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", id + ".json")));
        }
    }

    [Fact]
    public void TargetAndNonrequestedArtifactSymlinksAreRefusedWithoutFollowingThem()
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using (var targetWorkspace = new TemporaryWorkspace())
            {
                var scope = Path.Combine(targetWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
                Directory.CreateDirectory(scope);
                Directory.CreateDirectory(Path.Combine(targetWorkspace.Root, "outside"));
                var sentinelPath = targetWorkspace.Write(Path.Combine("outside", "sentinel.json"), "sentinel bytes must remain untouched\n");
                var sentinelBytes = File.ReadAllBytes(sentinelPath);
                var linkPath = Path.Combine(scope, "R-LINK.json");
                File.CreateSymbolicLink(linkPath, sentinelPath);
                var linkTarget = new FileInfo(linkPath).LinkTarget;
                var input = targetWorkspace.Write("target-link.json", RecordJson("R-LINK", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));

                foreach (var operation in new[] { "show", "validate" })
                {
                    var result = Invoke(CreateContext(targetWorkspace.Root), ["ruling", operation, "R-LINK", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format]);
                    Assert.Equal(1, result.ExitCode);
                    using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
                    Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
                    Assert.Equal("unavailable", output.RootElement.GetProperty("disposition").GetString());
                    Assert.Equal("ruling-unsafe-path", output.RootElement.GetProperty("cause").GetString());
                    Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("artifact_path").ValueKind);
                    Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("content_sha256").ValueKind);
                }

                var recordArgs = new[] { "ruling", "record", "--id", "R-LINK", "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var reads = new List<string>();
                using var writer = new StringWriter();
                var exit = RulingCommand.ExecuteRecord(CreateContext(targetWorkspace.Root), recordArgs.Skip(2).ToArray(), writer,
                    (operation, path) => { if (operation == "read") reads.Add(path); }, T0);
                Assert.Equal(1, exit);
                using (var output = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString()))
                {
                    Assert.Equal("unavailable", output.RootElement.GetProperty("disposition").GetString());
                    Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
                    Assert.Equal("ruling-unsafe-path", output.RootElement.GetProperty("cause").GetString());
                    Assert.False(output.RootElement.GetProperty("wrote").GetBoolean());
                    Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("artifact_path").ValueKind);
                    Assert.Equal(JsonValueKind.Null, output.RootElement.GetProperty("content_sha256").ValueKind);
                }
                Assert.Empty(reads);
                Assert.True(new FileInfo(linkPath).Exists);
                Assert.Equal(linkTarget, new FileInfo(linkPath).LinkTarget);
                Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinelPath));
                Assert.Empty(Directory.GetFiles(scope, "*.tmp"));
                Assert.Single(Directory.GetFiles(scope));
            }

            using (var inventoryWorkspace = new TemporaryWorkspace())
            {
                var context = CreateContext(inventoryWorkspace.Root);
                var goodInput = inventoryWorkspace.Write("good.json", RecordJson("R-GOOD", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
                var goodWrite = InvokeRecordAt(context, ["ruling", "record", "--id", "R-GOOD", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", goodInput, "--authority-role", "operator", "--write", "--format", "json"], T0);
                Assert.Equal(0, goodWrite.ExitCode);
                var scope = Path.Combine(inventoryWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
                var goodPath = Path.Combine(scope, "R-GOOD.json");
                var goodBytes = File.ReadAllBytes(goodPath);
                Directory.CreateDirectory(Path.Combine(inventoryWorkspace.Root, "outside"));
                var sentinelPath = inventoryWorkspace.Write(Path.Combine("outside", "inventory-sentinel.json"), "inventory sentinel\n");
                var sentinelBytes = File.ReadAllBytes(sentinelPath);
                var linkPath = Path.Combine(scope, "A-LINK.json");
                File.CreateSymbolicLink(linkPath, sentinelPath);
                var linkTarget = new FileInfo(linkPath).LinkTarget;

                foreach (var operation in new[] { "show", "validate" })
                {
                    var result = Invoke(context, ["ruling", operation, "R-GOOD", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format]);
                    Assert.Equal(1, result.ExitCode);
                    using var output = format == "json" ? JsonDocument.Parse(result.Output) : StructuredJsonFromMarkdown(result.Output);
                    Assert.Equal("unavailable", output.RootElement.GetProperty("disposition").GetString());
                    Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
                    Assert.Equal("ruling-unsafe-path", output.RootElement.GetProperty("cause").GetString());
                    Assert.Contains(output.RootElement.GetProperty("diagnostics").EnumerateArray(), item => item.GetProperty("path").GetString() == Path.GetRelativePath(inventoryWorkspace.Root, linkPath).Replace(Path.DirectorySeparatorChar, '/'));
                }

                var newInput = inventoryWorkspace.Write("new.json", RecordJson("R-NEW", T0.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
                var recordArgs = new[] { "ruling", "record", "--id", "R-NEW", "--domain", "intent-cli", "--team", "intent-cli-dev",
                    "--from-file", newInput, "--authority-role", "operator", "--write", "--format", format };
                var reads = new List<string>();
                using var writer = new StringWriter();
                var exit = RulingCommand.ExecuteRecord(context, recordArgs.Skip(2).ToArray(), writer,
                    (operation, path) => { if (operation == "read") reads.Add(path); }, T0.AddMinutes(1));
                Assert.Equal(1, exit);
                using (var output = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString()))
                {
                    Assert.Equal("unavailable", output.RootElement.GetProperty("disposition").GetString());
                    Assert.Equal("unavailable", output.RootElement.GetProperty("status").GetString());
                    Assert.Equal("ruling-unsafe-path", output.RootElement.GetProperty("cause").GetString());
                    Assert.False(output.RootElement.GetProperty("wrote").GetBoolean());
                }
                Assert.Empty(reads);
                Assert.Equal(goodBytes, File.ReadAllBytes(goodPath));
                Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinelPath));
                Assert.Equal(linkTarget, new FileInfo(linkPath).LinkTarget);
                Assert.False(File.Exists(Path.Combine(scope, "R-NEW.json")));
                Assert.Empty(Directory.GetFiles(scope, "*.tmp"));
            }
        }
    }

    [Fact]
    public void WholeInventoryCaseAliasesRefuseUnrelatedReadsAndAdmissions()
    {
        Assert.False(RulingArtifactStore.HasCaseInsensitiveIdentifierCollision(["R-A", "R-A", "R-B"]));
        Assert.False(RulingArtifactStore.HasCaseInsensitiveIdentifierCollision(["R-A", "R-B"]));
        Assert.True(RulingArtifactStore.HasCaseInsensitiveIdentifierCollision(["R-A", "r-a"]));

        // The current runner is case-insensitive on macOS; keep the pure
        // inventory predicate exercised there and create the actual paired
        // filesystem entries only where the volume can represent both names.
        if (!OperatingSystem.IsLinux()) return;

        // Each canonical positive is emitted by a writer in its own isolated
        // scope. Assemble the deliberately invalid Linux inventory only after
        // publication, since the canonical writer correctly rejects aliases.
        using var upperSource = new TemporaryWorkspace();
        using var lowerSource = new TemporaryWorkspace();
        var upper = Artifact("R-A", T0, []);
        var lower = Artifact("r-a", T0.AddSeconds(1), []);
        WriteArtifact(new RulingArtifactStore(upperSource.Root), upper);
        WriteArtifact(new RulingArtifactStore(lowerSource.Root), lower);

        using var workspace = new TemporaryWorkspace();
        var store = new RulingArtifactStore(workspace.Root);
        var scope = store.ScopeDirectory("intent-cli", "intent-cli-dev");
        Directory.CreateDirectory(scope);
        File.WriteAllBytes(Path.Combine(scope, "R-A.json"), File.ReadAllBytes(Path.Combine(upperSource.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-A.json")));
        File.WriteAllBytes(Path.Combine(scope, "r-a.json"), File.ReadAllBytes(Path.Combine(lowerSource.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "r-a.json")));
        var requested = store.Evaluate("intent-cli", "intent-cli-dev", "R-UNRELATED", T0.AddMinutes(1));
        Assert.Equal("conflict", requested.Status);
        Assert.Equal("ruling-identity-conflict", requested.Cause);
        Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev", Assert.Single(requested.Diagnostics).Path);

        var candidatePath = workspace.Write("candidate.json", RecordJson("R-NEW", "2026-10-10T12:01:00Z"));
        var command = Invoke(CreateContext(workspace.Root), ["ruling", "record", "--id", "R-NEW", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", candidatePath, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(1, command.ExitCode);
        using var output = JsonDocument.Parse(command.Output);
        Assert.Equal("refused", output.RootElement.GetProperty("disposition").GetString());
        Assert.Equal("conflict", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("ruling-identity-conflict", output.RootElement.GetProperty("cause").GetString());
        Assert.False(output.RootElement.GetProperty("wrote").GetBoolean());
        Assert.False(File.Exists(Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), "R-NEW.json")));
    }

    [Fact]
    public void ActualRecordResultsExposeOnlyVerifiedDiskEvidenceForReplayAndFailedWrites()
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using (var expiredWorkspace = new TemporaryWorkspace())
            {
                var expiration = T0.AddMinutes(1);
                var input = expiredWorkspace.Write("expired.json", RecordJson("R-EXPIRED-REPLAY", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
                    .Replace("\"expires_at\": null", $"\"expires_at\": \"{expiration:yyyy-MM-dd'T'HH:mm:ss'Z'}\"", StringComparison.Ordinal));
                var args = new[] { "ruling", "record", "--id", "R-EXPIRED-REPLAY", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                Assert.Equal(0, InvokeRecordAt(CreateContext(expiredWorkspace.Root), args, T0).ExitCode);
                var replay = InvokeRecordAt(CreateContext(expiredWorkspace.Root), args, expiration);
                Assert.Equal(1, replay.ExitCode);
                using var result = format == "json" ? JsonDocument.Parse(replay.Output) : StructuredJsonFromMarkdown(replay.Output);
                var root = result.RootElement;
                var persisted = File.ReadAllBytes(Path.Combine(expiredWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-EXPIRED-REPLAY.json"));
                Assert.Equal("refused", root.GetProperty("disposition").GetString());
                Assert.Equal("expired", root.GetProperty("status").GetString());
                Assert.True(root.GetProperty("expired").GetBoolean());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-EXPIRED-REPLAY.json", root.GetProperty("artifact_path").GetString());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(persisted)).ToLowerInvariant(), root.GetProperty("content_sha256").GetString());
                using var stored = JsonDocument.Parse(persisted);
                Assert.Equal(stored.RootElement.GetProperty("id").GetString(), root.GetProperty("normalized_record").GetProperty("id").GetString());
                Assert.Equal(stored.RootElement.GetProperty("expires_at").GetString(), root.GetProperty("normalized_record").GetProperty("expires_at").GetString());

                var changedInput = expiredWorkspace.Write("changed.json", RecordJson("R-EXPIRED-REPLAY", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), decision: "\"different immutable decision\"")
                    .Replace("\"expires_at\": null", $"\"expires_at\": \"{expiration:yyyy-MM-dd'T'HH:mm:ss'Z'}\"", StringComparison.Ordinal));
                var changedArgs = args.ToArray();
                changedArgs[Array.IndexOf(changedArgs, input)] = changedInput;
                var changedReplay = InvokeRecordAt(CreateContext(expiredWorkspace.Root), changedArgs, expiration);
                Assert.Equal(1, changedReplay.ExitCode);
                using var changedResult = format == "json" ? JsonDocument.Parse(changedReplay.Output) : StructuredJsonFromMarkdown(changedReplay.Output);
                Assert.Equal("ruling-content-conflict", changedResult.RootElement.GetProperty("cause").GetString());
                Assert.True(changedResult.RootElement.GetProperty("expired").GetBoolean());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(persisted)).ToLowerInvariant(), changedResult.RootElement.GetProperty("content_sha256").GetString());
                Assert.Equal("Keep the bounded choice.", changedResult.RootElement.GetProperty("normalized_record").GetProperty("decision").GetString());
            }

            using (var futureWorkspace = new TemporaryWorkspace())
            {
                var future = T0.AddMinutes(1);
                var input = futureWorkspace.Write("future.json", RecordJson("R-FUTURE-DISK-EVIDENCE", future.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
                var args = new[] { "ruling", "record", "--id", "R-FUTURE-DISK-EVIDENCE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var futureResult = InvokeRecordAt(CreateContext(futureWorkspace.Root), args, T0);
                Assert.Equal(1, futureResult.ExitCode);
                using var result = format == "json" ? JsonDocument.Parse(futureResult.Output) : StructuredJsonFromMarkdown(futureResult.Output);
                var root = result.RootElement;
                Assert.Equal("ruling-future-recorded-at", root.GetProperty("cause").GetString());
                Assert.False(root.GetProperty("wrote").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("artifact_path").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("content_sha256").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("expired").ValueKind);
            }

            using (var tempWorkspace = new TemporaryWorkspace())
            {
                var input = tempWorkspace.Write("candidate.json", RecordJson("R-FAILED-TEMP-EVIDENCE", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
                var args = new[] { "ruling", "record", "--id", "R-FAILED-TEMP-EVIDENCE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                using var writer = new StringWriter();
                var reached = false;
                var exit = RulingCommand.ExecuteRecord(CreateContext(tempWorkspace.Root), args[2..], writer, (operation, _) =>
                {
                    if (operation != "temp-write") return;
                    reached = true;
                    throw new IOException("reached prepublication failure");
                }, T0);
                Assert.True(reached);
                Assert.Equal(1, exit);
                using var result = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString());
                var root = result.RootElement;
                Assert.Equal("ruling-write-failed", root.GetProperty("cause").GetString());
                Assert.False(root.GetProperty("wrote").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("artifact_path").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("content_sha256").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("expired").ValueKind);
                Assert.False(File.Exists(Path.Combine(tempWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-FAILED-TEMP-EVIDENCE.json")));
            }

            using (var racedWorkspace = new TemporaryWorkspace())
            {
                var input = racedWorkspace.Write("candidate.json", RecordJson("R-RACE-NONCANONICAL", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
                var args = new[] { "ruling", "record", "--id", "R-RACE-NONCANONICAL", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var finalPath = Path.Combine(racedWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-RACE-NONCANONICAL.json");
                using var writer = new StringWriter();
                var reached = false;
                var exit = RulingCommand.ExecuteRecord(CreateContext(racedWorkspace.Root), args[2..], writer, (operation, path) =>
                {
                    if (operation != "rename") return;
                    reached = true;
                    Assert.Equal(finalPath, path);
                    File.WriteAllText(path, "not canonical ruling JSON", new UTF8Encoding(false));
                }, T0);
                Assert.True(reached);
                Assert.Equal(1, exit);
                using var result = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString());
                var root = result.RootElement;
                Assert.Equal("ruling-invalid-json", root.GetProperty("cause").GetString());
                Assert.False(root.GetProperty("wrote").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("artifact_path").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("content_sha256").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("expired").ValueKind);
                Assert.Equal("not canonical ruling JSON", File.ReadAllText(finalPath));
            }

            using (var racedWorkspace = new TemporaryWorkspace())
            using (var canonicalWorkspace = new TemporaryWorkspace())
            {
                var candidate = Artifact("R-RACE-DISK-EVIDENCE", T0, []);
                var diskRecord = candidate with { Decision = "independently published existing bytes", ExpiresAt = T0.AddMinutes(5) };
                WriteArtifact(new RulingArtifactStore(canonicalWorkspace.Root), diskRecord);
                var input = racedWorkspace.Write("candidate.json", Encoding.UTF8.GetString(RulingArtifact.Serialize(candidate)));
                var args = new[] { "ruling", "record", "--id", candidate.Id, "--domain", candidate.Domain, "--team", candidate.Team, "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var diskBytes = File.ReadAllBytes(Path.Combine(canonicalWorkspace.Root, ".intent-cli", "rulings", candidate.Domain, candidate.Team, candidate.Id + ".json"));
                var finalPath = Path.Combine(racedWorkspace.Root, ".intent-cli", "rulings", candidate.Domain, candidate.Team, candidate.Id + ".json");
                using var writer = new StringWriter();
                var reached = false;
                var exit = RulingCommand.ExecuteRecord(CreateContext(racedWorkspace.Root), args[2..], writer, (operation, path) =>
                {
                    if (operation != "rename") return;
                    reached = true;
                    Assert.Equal(finalPath, path);
                    File.WriteAllBytes(path, diskBytes);
                }, T0);
                Assert.True(reached);
                Assert.Equal(1, exit);
                using var result = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString());
                var root = result.RootElement;
                Assert.Equal("ruling-content-conflict", root.GetProperty("cause").GetString());
                Assert.False(root.GetProperty("wrote").GetBoolean());
                Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-RACE-DISK-EVIDENCE.json", root.GetProperty("artifact_path").GetString());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(diskBytes)).ToLowerInvariant(), root.GetProperty("content_sha256").GetString());
                Assert.Equal("independently published existing bytes", root.GetProperty("normalized_record").GetProperty("decision").GetString());
                Assert.False(root.GetProperty("expired").GetBoolean());
                Assert.Equal(diskBytes, File.ReadAllBytes(finalPath));
            }
        }
    }

    [Fact]
    public void TruncatedJsonIsNotMisclassifiedAsDepthAndNestedJsonIsDepthLimitedInBothFormats()
    {
        var malformedJson = new[] { "{", "{\"schema_version\":\"1\"", "{\"a\":[\"x\"]" };
        var valid = RecordJson("R-DEPTH-BOUNDARY", T0.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        var deeplyNested = valid.Replace("  \"supersedes\": []", "  \"unexpected\": " + new string('[', 17) + "0" + new string(']', 17) + ",\n  \"supersedes\": []", StringComparison.Ordinal);

        foreach (var (text, expectedCause, id) in malformedJson.Select((text, index) => (text, "ruling-invalid-json", "R-TRUNCATED-" + index))
                     .Append((deeplyNested, "ruling-depth-limit", "R-TOO-DEEP")))
        foreach (var format in new[] { "json", "markdown" })
        {
            using (var inputWorkspace = new TemporaryWorkspace())
            {
                var input = inputWorkspace.Write("bad.json", text);
                var args = new[] { "ruling", "record", "--id", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", input, "--authority-role", "operator", "--write", "--format", format };
                var command = Invoke(CreateContext(inputWorkspace.Root), args);
                Assert.Equal(1, command.ExitCode);
                using var result = format == "json" ? JsonDocument.Parse(command.Output) : StructuredJsonFromMarkdown(command.Output);
                Assert.Equal(expectedCause, result.RootElement.GetProperty("cause").GetString());
                Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
                Assert.False(Directory.Exists(Path.Combine(inputWorkspace.Root, ".intent-cli")));
            }

            using (var storedWorkspace = new TemporaryWorkspace())
            {
                var store = new RulingArtifactStore(storedWorkspace.Root);
                WriteArtifact(store, Artifact("R-STORED-DEPTH-CONTROL", T0, []));
                var badPath = Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), id + ".json");
                File.WriteAllText(badPath, text, new UTF8Encoding(false));
                var command = Invoke(CreateContext(storedWorkspace.Root), ["ruling", "show", "R-STORED-DEPTH-CONTROL", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", format]);
                Assert.Equal(1, command.ExitCode);
                using var result = format == "json" ? JsonDocument.Parse(command.Output) : StructuredJsonFromMarkdown(command.Output);
                Assert.Equal(expectedCause, result.RootElement.GetProperty("cause").GetString());
                Assert.Equal("unavailable", result.RootElement.GetProperty("status").GetString());
                Assert.Equal(Encoding.UTF8.GetBytes(text), File.ReadAllBytes(badPath));
            }
        }
    }

    [Fact]
    public void ActualCommandRechecksGraphAtPublicationBoundaryAndReportsKnownConflictInBothFormats()
    {
        foreach (var format in new[] { "json", "markdown" })
        {
            using var workspace = new TemporaryWorkspace();
            var store = new RulingArtifactStore(workspace.Root);
            WriteArtifact(store, Artifact("R-BASE", T0, []));
            var candidateJson = RecordJson("R-MERGE", "2026-10-10T12:02:00Z")
                .Replace("\"supersedes\": []", "\"supersedes\": [\"R-BASE\"]", StringComparison.Ordinal);
            var inputPath = workspace.Write("candidate.json", candidateJson);
            var newSuccessorWritten = false;
            var args = new[] { "ruling", "record", "--id", "R-MERGE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", format };
            using var writer = new StringWriter();
            var exitCode = RulingCommand.ExecuteRecord(CreateContext(workspace.Root), args[2..], writer, (operation, _) =>
            {
                if (operation == "rename" && !newSuccessorWritten)
                {
                    WriteArtifact(new RulingArtifactStore(workspace.Root), Artifact("R-CHILD", T0.AddMinutes(1), ["R-BASE"]));
                    newSuccessorWritten = true;
                }
            }, T0.AddMinutes(3));

            Assert.True(newSuccessorWritten);
            Assert.Equal(1, exitCode);
            using var result = format == "json" ? JsonDocument.Parse(writer.ToString()) : StructuredJsonFromMarkdown(writer.ToString());
            Assert.Equal("refused", result.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("conflict", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("ruling-supersession-conflict", result.RootElement.GetProperty("cause").GetString());
            Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-MERGE.json", result.RootElement.GetProperty("planned_artifact_path").GetString());
            Assert.Equal("R-MERGE", result.RootElement.GetProperty("normalized_record").GetProperty("id").GetString());
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("artifact_path").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("content_sha256").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("expired").ValueKind);
            Assert.True(File.Exists(Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), "R-BASE.json")));
            Assert.True(File.Exists(Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), "R-CHILD.json")));
            Assert.False(File.Exists(Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), "R-MERGE.json")));
        }
    }

    [Fact]
    public void DirectoryEnumerationErrorsAreStructuredAndDisappearedTempsAreIgnored()
    {
        using var workspace = new TemporaryWorkspace();
        var normal = new RulingArtifactStore(workspace.Root);
        WriteArtifact(normal, Artifact("R-LIST", T0, []));
        var scope = normal.ScopeDirectory("intent-cli", "intent-cli-dev");
        foreach (var operation in new[] { "evaluate", "write" })
        {
            var reached = false;
            var faulting = new RulingArtifactStore(workspace.Root, (phase, path) =>
            {
                if (phase == "list-directory" && path == scope)
                { reached = true; throw new UnauthorizedAccessException("reached ruling inventory listing"); }
            });
            if (operation == "evaluate")
            {
                var result = faulting.Evaluate("intent-cli", "intent-cli-dev", "R-LIST", T0.AddMinutes(1));
                Assert.Equal("unavailable", result.Status);
                Assert.Equal("ruling-path-unavailable", result.Cause);
            }
            else
            {
                var candidate = Artifact("R-LIST-NEW", T0.AddMinutes(1), []);
                Assert.False(faulting.TryWrite(candidate, RulingArtifact.Serialize(candidate), candidate.RecordedAt,
                    out var wrote, out _, out var cause, out _, out _));
                Assert.False(wrote);
                Assert.Equal("ruling-path-unavailable", cause);
            }
            Assert.True(reached);
        }

        var vanishedTemp = Path.Combine(scope, ".owned-by-another-writer.tmp");
        File.WriteAllText(vanishedTemp, "temporary");
        var removedTemp = false;
        var tolerant = new RulingArtifactStore(workspace.Root, (phase, path) =>
        {
            if (phase == "inventory-inspect" && path == vanishedTemp)
            { removedTemp = true; File.Delete(path); }
        });
        Assert.Equal("active", tolerant.Evaluate("intent-cli", "intent-cli-dev", "R-LIST", T0.AddMinutes(1)).Status);
        Assert.True(removedTemp);

        var another = new TemporaryWorkspace();
        using (another)
        {
            var store = new RulingArtifactStore(another.Root);
            WriteArtifact(store, Artifact("R-VANISH-JSON", T0, []));
            var jsonPath = Path.Combine(store.ScopeDirectory("intent-cli", "intent-cli-dev"), "R-VANISH-JSON.json");
            var removedJson = false;
            var deleting = new RulingArtifactStore(another.Root, (phase, path) =>
            {
                if (phase == "inventory-inspect" && path == jsonPath)
                { removedJson = true; File.Delete(path); }
            });
            var unavailable = deleting.Evaluate("intent-cli", "intent-cli-dev", "R-VANISH-JSON", T0.AddMinutes(1));
            Assert.True(removedJson);
            Assert.Equal("unavailable", unavailable.Status);
            Assert.Equal("ruling-artifact-unavailable", unavailable.Cause);
        }
    }

    [Fact]
    public async Task TargetAppearingBeforeRenameIsIdempotentOnlyForIdenticalActiveBytes()
    {
        static async Task<(bool Success, bool Wrote, bool Idempotent, string Cause)> Run(bool different)
        {
            using var workspace = new TemporaryWorkspace();
            using var arrived = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var record = Artifact("R-BEFORE-RENAME", T0, []);
            var winner = different ? record with { Decision = "other content" } : record;
            var loserStore = new RulingArtifactStore(workspace.Root, (phase, _) =>
            {
                if (phase == "temp-write")
                {
                    arrived.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("bounded pre-rename fixture wait expired");
                }
            });
            var loserTask = Task.Run(() =>
            {
                var success = loserStore.TryWrite(record, RulingArtifact.Serialize(record), T0,
                    out var wrote, out var idem, out var cause, out _, out _);
                return (success, wrote, idem, cause);
            });
            try
            {
                Assert.True(arrived.Wait(TimeSpan.FromSeconds(5)));
                var winnerStore = new RulingArtifactStore(workspace.Root);
                Assert.True(winnerStore.TryWrite(winner, RulingArtifact.Serialize(winner), T0,
                    out var winnerWrote, out _, out var winnerCause, out var winnerDetail, out _), $"{winnerCause}: {winnerDetail}");
                Assert.True(winnerWrote);
            }
            finally { release.Set(); }
            var result = await loserTask;
            return (result.success, result.wrote, result.idem, result.cause);
        }

        var identical = await Run(false);
        Assert.True(identical.Success);
        Assert.False(identical.Wrote);
        Assert.True(identical.Idempotent);
        var different = await Run(true);
        Assert.False(different.Success);
        Assert.False(different.Wrote);
        Assert.Equal("ruling-content-conflict", different.Cause);
    }

    [Fact]
    public void ChainReportsDirectSuccessorSeparatelyFromTerminalReplacement()
    {
        using var workspace = new TemporaryWorkspace();
        var store = new RulingArtifactStore(workspace.Root);
        WriteArtifact(store, Artifact("R-CHAIN-A", T0, []));
        WriteArtifact(store, Artifact("R-CHAIN-B", T0.AddMinutes(1), ["R-CHAIN-A"]));
        WriteArtifact(store, Artifact("R-CHAIN-C", T0.AddMinutes(2), ["R-CHAIN-B"]));

        var result = store.Evaluate("intent-cli", "intent-cli-dev", "R-CHAIN-A", T0.AddMinutes(3));
        Assert.Equal("superseded", result.Status);
        Assert.Equal(new[] { "R-CHAIN-B" }, result.SupersededBy);
        Assert.Equal(new[] { "R-CHAIN-C" }, result.ReplacementIds);
    }

    [Fact]
    public void ActualWriterFaultCallback_IsReachedBeforeRenameAndLeavesNoTarget()
    {
        using var workspace = new TemporaryWorkspace();
        var record = Artifact("R-FAULT", T0, []);
        var reached = new List<string>();
        var store = new RulingArtifactStore(workspace.Root, (operation, path) =>
        {
            reached.Add(operation + ":" + Path.GetFileName(path));
            if (operation == "rename") throw new IOException("fixture rename failure");
        });
        var success = store.TryWrite(record, RulingArtifact.Serialize(record), T0.AddMinutes(1), out var wrote, out _, out _, out _, out _);
        Assert.False(success);
        Assert.False(wrote);
        Assert.Contains(reached, x => x.StartsWith("rename:", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev")));
    }

    [Fact]
    public void TimestampExpiryAndReaderClockSkew_UseInclusiveExpiryAndFutureOnlyForNewCandidates()
    {
        Assert.True(RulingArtifact.TryTimestamp("2026-10-10T12:00:00Z", out var z));
        Assert.True(RulingArtifact.TryTimestamp("2026-10-10T12:00:00.0000000+00:00", out var offset));
        Assert.Equal(z, offset);
        foreach (var invalid in new[] { "2026-10-10", "12:00", "2026-10-10T12:00:00", "2026-10-10T12:00:00+01:00", "2026/10/10 12:00:00Z" })
            Assert.False(RulingArtifact.TryTimestamp(invalid, out _));

        using var workspace = new TemporaryWorkspace();
        var store = new RulingArtifactStore(workspace.Root);
        var expiring = Artifact("R-EXPIRES", T0, []) with { ExpiresAt = T0.AddSeconds(10) };
        WriteArtifact(store, expiring);
        Assert.Equal("active", store.Evaluate("intent-cli", "intent-cli-dev", expiring.Id, T0.AddSeconds(10).AddTicks(-1)).Status);
        Assert.Equal("expired", store.Evaluate("intent-cli", "intent-cli-dev", expiring.Id, T0.AddSeconds(10)).Status);

        var future = Artifact("R-FUTURE-STORED", T0.AddDays(1), []);
        WriteArtifact(store, future);
        Assert.Equal("active", store.Evaluate("intent-cli", "intent-cli-dev", future.Id, T0).Status);
        var futureCandidate = Artifact("R-FUTURE-NEW", T0.AddDays(1), []);
        var futureDecision = store.Evaluate("intent-cli", "intent-cli-dev", futureCandidate.Id, T0, futureCandidate, true);
        Assert.Equal("unavailable", futureDecision.Status);
        Assert.Equal("ruling-future-recorded-at", futureDecision.Cause);
    }

    [Fact]
    public void StrictCodec_AllowsRepositoryEmptyUnitsAndEnforcesDepthAndRawByteBoundary()
    {
        var repository = RecordJson("R-REPO", "2026-10-10T12:00:00Z", "[]")
            .Replace("\"kind\": \"execution-units\"", "\"kind\": \"repository\"", StringComparison.Ordinal);
        Assert.True(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(repository), false, out var repoRecord, out _, out _));
        Assert.Empty(repoRecord!.ExecutionUnits);

        var exactDepth = "{\"a\":" + string.Concat(Enumerable.Repeat("[", 15)) + "0" + string.Concat(Enumerable.Repeat("]", 15)) + "}";
        Assert.False(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(exactDepth), false, out _, out var depth16Cause, out _));
        Assert.Equal("ruling-invalid-input", depth16Cause);
        var tooDeep = "{\"a\":" + string.Concat(Enumerable.Repeat("[", 16)) + "0" + string.Concat(Enumerable.Repeat("]", 16)) + "}";
        Assert.False(RulingArtifact.TryParse(Encoding.UTF8.GetBytes(tooDeep), false, out _, out var depth17Cause, out _));
        Assert.Equal("ruling-depth-limit", depth17Cause);

        var valid = Encoding.UTF8.GetBytes(RecordJson("R-BOUND", "2026-10-10T12:00:00Z"));
        var exact = new byte[RulingArtifact.MaximumBytes];
        valid.CopyTo(exact, 0);
        Array.Fill(exact, (byte)' ', valid.Length, exact.Length - valid.Length);
        Assert.True(RulingArtifact.TryParse(exact, false, out _, out _, out _));
        var over = new byte[RulingArtifact.MaximumBytes + 1];
        exact.CopyTo(over, 0);
        over[^1] = (byte)' ';
        Assert.False(RulingArtifact.TryParse(over, false, out _, out var sizeCause, out _));
        Assert.Equal("ruling-size-limit", sizeCause);
    }

    [Fact]
    public void SchemaBoundaries_AcceptExactLimitsAndRejectOneOver()
    {
        bool Parses(string json, out string cause) =>
            RulingArtifact.TryParse(Encoding.UTF8.GetBytes(json), false, out _, out cause, out _);

        Assert.True(RulingArtifact.TryIdentifier(new string('A', 128), out _));
        Assert.False(RulingArtifact.TryIdentifier(new string('A', 129), out _));
        foreach (var unsafeIdentifier in new[] { ".hidden", "..", "has/slash", "has\\\\slash", " leading", "trailing " })
            Assert.False(RulingArtifact.TryIdentifier(unsafeIdentifier, out _));

        var validId = new string('A', 128);
        Assert.True(Parses(RecordJson(validId, "2026-10-10T12:00:00Z"), out _));
        Assert.False(Parses(RecordJson(validId + "A", "2026-10-10T12:00:00Z"), out var idCause));
        Assert.Equal("ruling-invalid-input", idCause);

        var units128 = Enumerable.Range(0, 128).Select(i => "U" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var exactUnits = RecordJson("R-UNITS-128", "2026-10-10T12:00:00Z", System.Text.Json.JsonSerializer.Serialize(units128));
        Assert.True(Parses(exactUnits, out _));
        var overUnits = RecordJson("R-UNITS-129", "2026-10-10T12:00:00Z", System.Text.Json.JsonSerializer.Serialize(units128.Append("U128")));
        Assert.False(Parses(overUnits, out var unitCause));
        Assert.Equal("ruling-invalid-input", unitCause);

        string EvidenceArray(int count) => string.Join(", ", Enumerable.Range(0, count).Select(i => System.Text.Json.JsonSerializer.Serialize("evidence-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.True(Parses(RecordJson("R-REFS-64", "2026-10-10T12:00:00Z", evidence: EvidenceArray(64)), out _));
        Assert.False(Parses(RecordJson("R-REFS-65", "2026-10-10T12:00:00Z", evidence: EvidenceArray(65)), out var evidenceCountCause));
        Assert.Equal("ruling-invalid-input", evidenceCountCause);

        string SupersedesArray(int count) => "\"supersedes\": [" + string.Join(", ", Enumerable.Range(0, count).Select(i => System.Text.Json.JsonSerializer.Serialize("S" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)))) + "]";
        var supersedesBase = RecordJson("R-SUPERSEDES", "2026-10-10T12:00:00Z");
        Assert.True(Parses(supersedesBase.Replace("\"supersedes\": []", SupersedesArray(500), StringComparison.Ordinal), out _));
        Assert.False(Parses(supersedesBase.Replace("\"supersedes\": []", SupersedesArray(501), StringComparison.Ordinal), out var supersedesCause));
        Assert.Equal("ruling-invalid-input", supersedesCause);

        var decisionExact = RecordJson("R-TEXT-EXACT", "2026-10-10T12:00:00Z", decision: System.Text.Json.JsonSerializer.Serialize(new string('d', 16_384)), rationale: System.Text.Json.JsonSerializer.Serialize(new string('r', 16_384)), evidence: System.Text.Json.JsonSerializer.Serialize(new string('e', 2_048)));
        Assert.True(Parses(decisionExact, out _));
        var decisionOver = RecordJson("R-TEXT-OVER", "2026-10-10T12:00:00Z", decision: System.Text.Json.JsonSerializer.Serialize(new string('d', 16_385)));
        Assert.False(Parses(decisionOver, out var textCause));
        Assert.Equal("ruling-invalid-input", textCause);
        var evidenceOver = RecordJson("R-EVIDENCE-OVER", "2026-10-10T12:00:00Z", evidence: System.Text.Json.JsonSerializer.Serialize(new string('e', 2_049)));
        Assert.False(Parses(evidenceOver, out var evidenceLengthCause));
        Assert.Equal("ruling-invalid-input", evidenceLengthCause);

        var supplementaryExact = RecordJson("R-SURROGATE", "2026-10-10T12:00:00Z", decision: System.Text.Json.JsonSerializer.Serialize(new string('a', 16_382) + "😀"));
        Assert.True(Parses(supplementaryExact, out _));
        var supplementaryOver = RecordJson("R-SURROGATE-OVER", "2026-10-10T12:00:00Z", decision: System.Text.Json.JsonSerializer.Serialize(new string('a', 16_383) + "😀"));
        Assert.False(Parses(supplementaryOver, out var supplementaryCause));
        Assert.Equal("ruling-invalid-input", supplementaryCause);
    }

    [Fact]
    public void NormalizedExpansionOverOneMiB_IsRefusedBeforeDirectoryCreation()
    {
        using (var exactWorkspace = new TemporaryWorkspace())
        {
            var baseline = Artifact("R-NORMALIZED-EXACT", T0, []) with { Decision = "x" };
            var baselineLength = RulingArtifact.Serialize(baseline).Length;
            var expansion = RulingArtifact.MaximumBytes - baselineLength + 1;
            var escapedCount = expansion / 6;
            var asciiCount = expansion % 6;
            var exact = baseline with { Decision = new string('\u3000', escapedCount) + new string('x', asciiCount) };
            var exactBytes = RulingArtifact.Serialize(exact);
            Assert.Equal(RulingArtifact.MaximumBytes, exactBytes.Length);
            var store = new RulingArtifactStore(exactWorkspace.Root);
            Assert.True(store.TryWrite(exact, exactBytes, T0, out var wrote, out _, out var exactCause, out var exactDetail, out _), $"{exactCause}: {exactDetail}");
            Assert.True(wrote);
            var exactPath = Path.Combine(exactWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", exact.Id + ".json");
            Assert.Equal(exactBytes, File.ReadAllBytes(exactPath));
        }

        using var workspace = new TemporaryWorkspace();
        var decision = new string('\u3000', 16383) + "A";
        var rationale = new string('\u3000', 16383) + "B";
        var refs = Enumerable.Range(0, 64).Select(i => new string('\u3000', 2047) + (char)(0x3041 + i)).ToArray();
        var supersedes = Enumerable.Range(0, 500).Select(i => "P" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('X', 124)).ToArray();
        var input = "{\"schema_version\":\"1\",\"id\":\"R-LARGE\",\"domain\":\"intent-cli\",\"team\":\"intent-cli-dev\",\"authority_role\":\"operator\",\"scope\":{\"target_repo\":\"J-Tech-Japan/intent-system\",\"kind\":\"execution-units\",\"execution_units\":[\"G862\"]},\"decision\":\"" + decision + "\",\"rationale\":\"" + rationale + "\",\"evidence_refs\":[" + string.Join(',', refs.Select(x => "\"" + x + "\"")) + "],\"recorded_at\":\"2026-10-10T11:00:00Z\",\"expires_at\":null,\"supersedes\":[" + string.Join(',', supersedes.Select(x => "\"" + x + "\"")) + "]}";
        var inputBytes = Encoding.UTF8.GetBytes(input);
        Assert.InRange(inputBytes.Length, 1, RulingArtifact.MaximumBytes);
        Assert.True(RulingArtifact.TryParse(inputBytes, false, out var parsed, out var cause, out var detail), $"{cause}: {detail}");
        Assert.True(RulingArtifact.Serialize(parsed!).Length > RulingArtifact.MaximumBytes);
        var inputPath = workspace.Write("large.json", input);
        var (exit, output) = Invoke(CreateContext(workspace.Root), ["ruling", "record", "--id", "R-LARGE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", "json"]);
        Assert.Equal(1, exit);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("ruling-normalized-size-limit", result.RootElement.GetProperty("cause").GetString());
        Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
    }

    [Fact]
    public void CaseAliasIsIdentityConflictAndReadFaultRemainsUnavailable()
    {
        using var workspace = new TemporaryWorkspace();
        var alias = Path.Combine(workspace.Root, ".intent-cli", "rulings", "Intent-CLI", "intent-cli-dev");
        Directory.CreateDirectory(alias);
        var store = new RulingArtifactStore(workspace.Root);
        var collision = store.Evaluate("intent-cli", "intent-cli-dev", "R-ALIAS", T0);
        Assert.Equal("conflict", collision.Status);
        Assert.Equal("ruling-identity-conflict", collision.Cause);

        var clean = new TemporaryWorkspace();
        using (clean)
        {
            var normalStore = new RulingArtifactStore(clean.Root);
            WriteArtifact(normalStore, Artifact("R-READ", T0, []));
            var reached = false;
            var faulting = new RulingArtifactStore(clean.Root, (operation, _) =>
            {
                if (operation == "read") { reached = true; throw new UnauthorizedAccessException("reached read fault"); }
            });
            var unavailable = faulting.Evaluate("intent-cli", "intent-cli-dev", "R-READ", T0.AddMinutes(1));
            Assert.True(reached);
            Assert.Equal("unavailable", unavailable.Status);
            Assert.Equal("ruling-artifact-unavailable", unavailable.Cause);
            Assert.Contains(unavailable.Diagnostics, x => x.Path?.EndsWith("R-READ.json", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void SeventeenTipsAndExpiredTip_RequireExplicitAllTipMergeWithoutRewritingHistory()
    {
        using var workspace = new TemporaryWorkspace();
        var baseTime = DateTimeOffset.UtcNow.AddHours(-3);
        var remote = CreateBareRulingRemote(workspace, "seventeen-tips", [Artifact("R-ROOT", baseTime, [])]);
        var originalRoot = ReadRulingBytes(remote.SeedClone, "R-ROOT");
        var tips = new List<string>();
        for (var i = 0; i < 17; i++)
        {
            var id = $"R-TIP-{i:D2}";
            var branchName = "tip-" + i.ToString("D2");
            var branch = CloneRulingRemote(workspace, remote, "writer-" + i.ToString("D2"));
            RunGit(branch, "switch", "-c", branchName);
            var tip = Artifact(id, baseTime.AddMinutes(i + 1), ["R-ROOT"]);
            if (i == 0) tip = tip with { ExpiresAt = baseTime.AddMinutes(10) };
            WriteActualRuling(workspace, branch, tip, baseTime.AddMinutes(i + 2));
            CommitAndPushCurrentBranch(branch, branchName, "publish " + id);
            tips.Add(id);
        }
        var aggregate = CloneRulingRemote(workspace, remote, "seventeen-aggregate");
        MergePublishedBranches(aggregate, Enumerable.Range(0, 17).Select(i => "tip-" + i.ToString("D2")));
        var originalPaths = new[] { "R-ROOT" }.Concat(tips).ToArray();
        var before = originalPaths.ToDictionary(id => id, id => ReadRulingBytes(aggregate, id), StringComparer.Ordinal);
        Assert.Equal(originalRoot, before["R-ROOT"]);

        var conflict = InvokeReadAt(CreateContext(aggregate), "show", ["ruling", "show", "R-ROOT", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], DateTimeOffset.UtcNow);
        Assert.Equal(1, conflict.ExitCode);
        using (var conflictJson = JsonDocument.Parse(conflict.Output))
        {
            Assert.Equal("conflict", conflictJson.RootElement.GetProperty("status").GetString());
            Assert.Equal("ruling-successor-conflict", conflictJson.RootElement.GetProperty("cause").GetString());
            Assert.Equal(tips.Order(StringComparer.Ordinal), conflictJson.RootElement.GetProperty("superseded_by").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(tips.Order(StringComparer.Ordinal), conflictJson.RootElement.GetProperty("replacement_ids").EnumerateArray().Select(x => x.GetString()));
        }

        var partial = Artifact("R-MERGE-PARTIAL", baseTime.AddHours(2), tips.Take(16).ToArray());
        var partialResult = InvokeActualRuling(workspace, aggregate, partial, baseTime.AddHours(2));
        Assert.Equal(1, partialResult.ExitCode);
        using (var partialJson = JsonDocument.Parse(partialResult.Output))
        {
            Assert.Equal("refused", partialJson.RootElement.GetProperty("disposition").GetString());
            Assert.Equal("conflict", partialJson.RootElement.GetProperty("status").GetString());
            Assert.Equal("ruling-incomplete-merge", partialJson.RootElement.GetProperty("cause").GetString());
            Assert.False(partialJson.RootElement.GetProperty("wrote").GetBoolean());
        }
        Assert.False(File.Exists(RulingPath(aggregate, partial.Id)));

        var complete = Artifact("R-MERGE-ALL", baseTime.AddHours(2), tips);
        var completeResult = InvokeActualRuling(workspace, aggregate, complete, baseTime.AddHours(2));
        Assert.Equal(0, completeResult.ExitCode);
        using (var completeJson = JsonDocument.Parse(completeResult.Output))
        {
            Assert.Equal("written", completeJson.RootElement.GetProperty("disposition").GetString());
            Assert.True(completeJson.RootElement.GetProperty("wrote").GetBoolean());
        }
        Assert.Equal(RulingArtifact.Serialize(complete), File.ReadAllBytes(RulingPath(aggregate, complete.Id)));
        CommitAndPushCurrentBranch(aggregate, "main", "publish all-tip merge");

        var rootAfterMerge = InvokeReadAt(CreateContext(aggregate), "show", ["ruling", "show", "R-ROOT", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], DateTimeOffset.UtcNow);
        Assert.Equal(0, rootAfterMerge.ExitCode);
        using (var rootJson = JsonDocument.Parse(rootAfterMerge.Output))
        {
            Assert.Equal("superseded", rootJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(tips.Order(StringComparer.Ordinal), rootJson.RootElement.GetProperty("superseded_by").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(new[] { complete.Id }, rootJson.RootElement.GetProperty("replacement_ids").EnumerateArray().Select(x => x.GetString()));
        }
        var expiredTip = InvokeReadAt(CreateContext(aggregate), "show", ["ruling", "show", tips[0], "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], DateTimeOffset.UtcNow);
        Assert.Equal(0, expiredTip.ExitCode);
        using (var expiredJson = JsonDocument.Parse(expiredTip.Output))
        {
            Assert.Equal("superseded", expiredJson.RootElement.GetProperty("status").GetString());
            Assert.True(expiredJson.RootElement.GetProperty("expired").GetBoolean());
        }

        Assert.Equal(originalRoot, ReadRulingBytes(aggregate, "R-ROOT"));
        foreach (var id in tips)
            Assert.Equal(before[id], ReadRulingBytes(aggregate, id));
    }

    [Fact]
    public void WeakComponentJoin_RequiresTipsFromEveryConnectedBranch()
    {
        using var workspace = new TemporaryWorkspace();
        var baseTime = DateTimeOffset.UtcNow.AddHours(-3);
        var remote = CreateBareRulingRemote(workspace, "weak-join", [Artifact("R-A", baseTime, []), Artifact("R-X", baseTime, [])]);

        var joinBranch = CloneRulingRemote(workspace, remote, "join-writer");
        RunGit(joinBranch, "switch", "-c", "join-branch");
        WriteActualRuling(workspace, joinBranch, Artifact("R-B", baseTime.AddMinutes(1), ["R-A"]), baseTime.AddMinutes(3));
        WriteActualRuling(workspace, joinBranch, Artifact("R-Y", baseTime.AddMinutes(1), ["R-X"]), baseTime.AddMinutes(3));
        WriteActualRuling(workspace, joinBranch, Artifact("R-J", baseTime.AddMinutes(2), ["R-B", "R-Y"]), baseTime.AddMinutes(3));
        CommitAndPushCurrentBranch(joinBranch, "join-branch", "publish joined branch");

        var otherBranch = CloneRulingRemote(workspace, remote, "other-writer");
        RunGit(otherBranch, "switch", "-c", "other-branch");
        WriteActualRuling(workspace, otherBranch, Artifact("R-C", baseTime.AddMinutes(1), ["R-A"]), baseTime.AddMinutes(3));
        WriteActualRuling(workspace, otherBranch, Artifact("R-Z", baseTime.AddMinutes(1), ["R-X"]), baseTime.AddMinutes(3));
        CommitAndPushCurrentBranch(otherBranch, "other-branch", "publish alternate branch");

        var aggregate = CloneRulingRemote(workspace, remote, "join-aggregate");
        MergePublishedBranches(aggregate, ["join-branch", "other-branch"]);
        var priorIds = new[] { "R-A", "R-X", "R-B", "R-Y", "R-J", "R-C", "R-Z" };
        var before = priorIds.ToDictionary(id => id, id => ReadRulingBytes(aggregate, id), StringComparer.Ordinal);

        foreach (var (id, terminals) in new[] { ("R-A", new[] { "R-C", "R-J" }), ("R-X", new[] { "R-J", "R-Z" }) })
        {
            var conflict = InvokeReadAt(CreateContext(aggregate), "show", ["ruling", "show", id, "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], DateTimeOffset.UtcNow);
            Assert.Equal(1, conflict.ExitCode);
            using var conflictJson = JsonDocument.Parse(conflict.Output);
            Assert.Equal("ruling-successor-conflict", conflictJson.RootElement.GetProperty("cause").GetString());
            Assert.Equal(terminals, conflictJson.RootElement.GetProperty("replacement_ids").EnumerateArray().Select(x => x.GetString()));
        }

        var partial = Artifact("R-D-PARTIAL", baseTime.AddMinutes(3), ["R-C", "R-J"]);
        var partialResult = InvokeActualRuling(workspace, aggregate, partial, baseTime.AddMinutes(4));
        Assert.Equal(1, partialResult.ExitCode);
        using (var partialJson = JsonDocument.Parse(partialResult.Output))
        {
            Assert.Equal("ruling-incomplete-merge", partialJson.RootElement.GetProperty("cause").GetString());
            Assert.False(partialJson.RootElement.GetProperty("wrote").GetBoolean());
        }
        Assert.False(File.Exists(RulingPath(aggregate, partial.Id)));

        var repair = Artifact("R-D", baseTime.AddMinutes(3), ["R-C", "R-J", "R-Z"]);
        var written = InvokeActualRuling(workspace, aggregate, repair, baseTime.AddMinutes(4));
        Assert.Equal(0, written.ExitCode);
        using (var result = JsonDocument.Parse(written.Output)) Assert.True(result.RootElement.GetProperty("wrote").GetBoolean());
        CommitAndPushCurrentBranch(aggregate, "main", "publish complete weak-component merge");

        var after = InvokeReadAt(CreateContext(aggregate), "show", ["ruling", "show", "R-X", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"], DateTimeOffset.UtcNow);
        Assert.Equal(0, after.ExitCode);
        using (var afterJson = JsonDocument.Parse(after.Output))
        {
            Assert.Equal("superseded", afterJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(new[] { "R-Y", "R-Z" }, afterJson.RootElement.GetProperty("superseded_by").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(new[] { "R-D" }, afterJson.RootElement.GetProperty("replacement_ids").EnumerateArray().Select(x => x.GetString()));
        }
        foreach (var id in priorIds) Assert.Equal(before[id], ReadRulingBytes(aggregate, id));
    }

    [Fact]
    public void CommandParserAndAuthorityRefusals_AreExplicitAndDoNotCreateState()
    {
        using var workspace = new TemporaryWorkspace();
        var context = CreateContext(workspace.Root);
        var inputPath = workspace.Write("unsupported-authority.json", RecordJson("R-AUTH", "2026-10-10T11:00:00Z")
            .Replace("\"authority_role\": \"operator\"", "\"authority_role\": \"architect\"", StringComparison.Ordinal));
        var invalidAuthority = Invoke(context, ["ruling", "record", "--id", "R-AUTH", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "architect", "--format", "json"]);
        Assert.Equal(1, invalidAuthority.ExitCode);
        using (var result = JsonDocument.Parse(invalidAuthority.Output))
        {
            Assert.Equal("ruling-invalid-authority", result.RootElement.GetProperty("cause").GetString());
            Assert.False(result.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Null(result.RootElement.GetProperty("artifact_path").GetString());
        }
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));

        var missing = Invoke(context, ["ruling", "show", "R-MISSING", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
        Assert.Equal(1, missing.ExitCode);
        using (var result = JsonDocument.Parse(missing.Output))
        {
            Assert.Equal("missing", result.RootElement.GetProperty("status").GetString());
            Assert.Equal("refused", result.RootElement.GetProperty("disposition").GetString());
            Assert.Null(result.RootElement.GetProperty("artifact_path").GetString());
        }
        var forbiddenWriteFlag = Invoke(context, ["ruling", "show", "R-MISSING", "--domain", "intent-cli", "--team", "intent-cli-dev", "--write"]);
        Assert.Equal(1, forbiddenWriteFlag.ExitCode);
        Assert.Contains("unknown option", forbiddenWriteFlag.Output, StringComparison.Ordinal);

        var parserCases = new[]
        {
            new[] { "ruling", "record", "--id" },
            new[] { "ruling", "record", "--id", "R-AUTH", "--id", "R-AUTH", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator" },
            new[] { "ruling", "record", "--id", "R-AUTH", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", inputPath, "--authority-role", "operator", "--format", "yaml" },
            new[] { "ruling", "show", "R-AUTH", "--domain", "intent-cli", "--team", "intent-cli-dev", "--write" },
            new[] { "ruling", "show", "R-AUTH", "--team", "intent-cli-dev" },
        };
        foreach (var args in parserCases)
        {
            var refused = Invoke(context, args);
            Assert.Equal(1, refused.ExitCode);
            Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
        }

        var validInputPath = workspace.Write("valid-operator.json", RecordJson("R-VALID-PARSER", "2026-10-10T11:00:00Z"));
        var flagCases = new[]
        {
            (new[] { "--write", "--dry-run" }, "--write and --dry-run are mutually exclusive"),
            (new[] { "--write", "true" }, "--write does not take a value"),
            (new[] { "--dry-run", "true" }, "--dry-run does not take a value"),
        };
        foreach (var (flags, expectedError) in flagCases)
        {
            var args = new[] { "ruling", "record", "--id", "R-VALID-PARSER", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", validInputPath, "--authority-role", "operator" }
                .Concat(flags).Append("--format").Append("json").ToArray();
            var refused = Invoke(context, args);
            Assert.Equal(1, refused.ExitCode);
            Assert.Contains(expectedError, refused.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".intent-cli")));
        }
    }

    [Fact]
    public void InventoryLimitAllows500CompleteRecordsAndRefuses501IncludingCandidate()
    {
        using var workspace = new TemporaryWorkspace();
        var scope = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
        Directory.CreateDirectory(scope);
        for (var i = 0; i < 501; i++)
        {
            var seedRoot = workspace.CreateChild("seed-" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            var id = "R-" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            var record = Artifact(id, T0, []);
            WriteArtifact(new RulingArtifactStore(seedRoot), record);
            File.Copy(Path.Combine(seedRoot, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", id + ".json"), Path.Combine(scope, id + ".json"));
        }
        var store = new RulingArtifactStore(workspace.Root);
        foreach (var malformedId in new[] { "R-000", "R-500" })
        {
            var malformedPath = Path.Combine(scope, malformedId + ".json");
            var canonicalBefore = File.ReadAllBytes(malformedPath);
            try
            {
                File.WriteAllText(malformedPath, "{ truncated before inventory limit", new UTF8Encoding(false));
                var contentReads = 0;
                var bounded = new RulingArtifactStore(workspace.Root, (operation, _) =>
                { if (operation == "read") contentReads++; });
                var overLimit = bounded.Evaluate("intent-cli", "intent-cli-dev", "R-000", T0);
                Assert.Equal("unavailable", overLimit.Status);
                Assert.Equal("ruling-inventory-limit", overLimit.Cause);
                Assert.Empty(overLimit.Records);
                Assert.Equal(0, contentReads);

                if (malformedId == "R-000")
                {
                    var candidateInput = workspace.Write("inventory-overflow-candidate.json", RecordJson("R-CMD-LIMIT", "2026-10-10T12:01:00Z"));
                    var commandArgs = new[] { "ruling", "record", "--id", "R-CMD-LIMIT", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", candidateInput, "--authority-role", "operator", "--write", "--format", "json" };
                    var commandReads = 0;
                    using var commandWriter = new StringWriter();
                    var commandExit = RulingCommand.ExecuteRecord(CreateContext(workspace.Root), commandArgs[2..], commandWriter,
                        (operation, _) => { if (operation == "read") commandReads++; }, T0.AddMinutes(1));
                    Assert.Equal(0, commandReads);
                    Assert.Equal(1, commandExit);
                    using var commandResult = JsonDocument.Parse(commandWriter.ToString());
                    Assert.Equal("unavailable", commandResult.RootElement.GetProperty("status").GetString());
                    Assert.Equal("ruling-inventory-limit", commandResult.RootElement.GetProperty("cause").GetString());
                    Assert.False(commandResult.RootElement.GetProperty("wrote").GetBoolean());
                    Assert.False(File.Exists(Path.Combine(scope, "R-CMD-LIMIT.json")));
                }
            }
            finally { File.WriteAllBytes(malformedPath, canonicalBefore); }
        }
        var at499 = new TemporaryWorkspace();
        using (at499)
        {
            var target = Path.Combine(at499.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(scope).OrderBy(Path.GetFileName, StringComparer.Ordinal).Take(499))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            var allowed = new RulingArtifactStore(at499.Root).Evaluate("intent-cli", "intent-cli-dev", "R-000", T0);
            Assert.Equal("active", allowed.Status);
        }
        var at500 = new TemporaryWorkspace();
        using (at500)
        {
            var target = Path.Combine(at500.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(scope).OrderBy(Path.GetFileName, StringComparer.Ordinal).Take(500)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            var allowed = new RulingArtifactStore(at500.Root).Evaluate("intent-cli", "intent-cli-dev", "R-000", T0);
            Assert.Equal("active", allowed.Status);
            var candidate = Artifact("R-CANDIDATE", T0.AddMinutes(1), []);
            var newCandidateReads = 0;
            var candidateLimit = new RulingArtifactStore(at500.Root, (operation, _) =>
            { if (operation == "read") newCandidateReads++; }).Evaluate("intent-cli", "intent-cli-dev", candidate.Id, T0.AddMinutes(1), candidate, true);
            Assert.Equal("unavailable", candidateLimit.Status);
            Assert.Equal("ruling-inventory-limit", candidateLimit.Cause);
            Assert.Empty(candidateLimit.Records);
            Assert.Equal(0, newCandidateReads);

            var existingReplayReads = 0;
            var existingReplay = new RulingArtifactStore(at500.Root, (operation, _) =>
            { if (operation == "read") existingReplayReads++; }).Evaluate("intent-cli", "intent-cli-dev", "R-000", T0,
                Artifact("R-000", T0, []), forAdmission: true);
            Assert.Equal("active", existingReplay.Status);
            Assert.Equal(500, existingReplayReads);

            var candidatePath = at500.Write("capacity-candidate.json", RecordJson("R-CAPACITY-CANDIDATE", "2026-10-10T12:01:00Z"));
            var commandArgs = new[] { "ruling", "record", "--id", "R-CAPACITY-CANDIDATE", "--domain", "intent-cli", "--team", "intent-cli-dev", "--from-file", candidatePath, "--authority-role", "operator", "--write", "--format", "json" };
            var commandContentReads = 0;
            using var commandWriter = new StringWriter();
            var commandExit = RulingCommand.ExecuteRecord(CreateContext(at500.Root), commandArgs[2..], commandWriter,
                (operation, _) => { if (operation == "read") commandContentReads++; }, T0.AddMinutes(2));
            Assert.Equal(0, commandContentReads);
            Assert.Equal(1, commandExit);
            using var commandResult = JsonDocument.Parse(commandWriter.ToString());
            Assert.Equal("unavailable", commandResult.RootElement.GetProperty("status").GetString());
            Assert.Equal("ruling-inventory-limit", commandResult.RootElement.GetProperty("cause").GetString());
            Assert.Contains("responsible host operator", commandResult.RootElement.GetProperty("recovery_hint").GetString(), StringComparison.Ordinal);
            Assert.Contains("Do not partially merge or archive", commandResult.RootElement.GetProperty("recovery_hint").GetString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(target, "R-CAPACITY-CANDIDATE.json")));
        }
        var over = store.Evaluate("intent-cli", "intent-cli-dev", "R-000", T0);
        Assert.Equal("unavailable", over.Status);
        Assert.Equal("ruling-inventory-limit", over.Cause);
        Assert.Empty(over.ReplacementIds);
    }

    [Fact]
    public void ActualCommandWritesSchemaValidNormalizedArtifactAtExactOneMiBAndRefusesOneByteOver()
    {
        const int predecessorCount = 400;
        using var workspace = new TemporaryWorkspace();
        var targetScope = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
        Directory.CreateDirectory(targetScope);
        var units = Enumerable.Range(0, 128)
            .Select(index => "U" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('A', 124))
            .ToArray();
        var predecessorIds = Enumerable.Range(0, predecessorCount)
            .Select(index => "P" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('B', 124))
            .ToArray();

        for (var index = 0; index < predecessorCount; index++)
        {
            var predecessor = Artifact(predecessorIds[index], T0, []) with { ExecutionUnits = units };
            var seedRoot = workspace.CreateChild("exact-size-seed-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            var input = workspace.Write("seed-input-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".json", Encoding.UTF8.GetString(RulingArtifact.Serialize(predecessor)));
            var args = new[] { "ruling", "record", "--id", predecessor.Id, "--domain", predecessor.Domain, "--team", predecessor.Team, "--from-file", input, "--authority-role", "operator", "--write", "--format", "json" };
            var written = InvokeRecordAt(CreateContext(seedRoot), args, T0);
            Assert.Equal(0, written.ExitCode);
            using var result = JsonDocument.Parse(written.Output);
            Assert.True(result.RootElement.GetProperty("wrote").GetBoolean());
            var emitted = Path.Combine(seedRoot, ".intent-cli", "rulings", predecessor.Domain, predecessor.Team, predecessor.Id + ".json");
            Assert.True(File.Exists(emitted));
            File.Copy(emitted, Path.Combine(targetScope, predecessor.Id + ".json"));
        }

        var evidence = Enumerable.Range(0, 64)
            .Select(index => "E" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('\u0080', 2044))
            .ToArray();
        var candidate = Artifact("R-EXACT-1MIB", T0.AddMinutes(1), predecessorIds) with
        {
            ExecutionUnits = units,
            Decision = new string('\u0080', 16384),
            Rationale = new string('\u0080', 16384),
            EvidenceRefs = evidence,
        };
        var baseLength = RulingArtifact.Serialize(candidate).Length;
        var excess = baseLength - RulingArtifact.MaximumBytes;
        Assert.True(excess > 0, $"The measured seed record was not over the normalized byte limit: {baseLength} bytes.");
        var replaceEscapes = excess / 5;
        var removeAscii = excess % 5;
        Assert.InRange(replaceEscapes, removeAscii, candidate.Decision.Length);
        candidate = candidate with
        {
            Decision = new string('x', replaceEscapes - removeAscii) + new string('\u0080', candidate.Decision.Length - replaceEscapes),
        };
        var exactBytes = RulingArtifact.Serialize(candidate);
        Assert.Equal(RulingArtifact.MaximumBytes, exactBytes.Length);
        Assert.True(RulingArtifact.TryParse(exactBytes, true, out var parsedExact, out var exactCause, out var exactDetail), $"{exactCause}: {exactDetail}");
        Assert.Equal(exactBytes, RulingArtifact.Serialize(parsedExact!));

        var exactInput = workspace.Write("exact-one-mib.json", Encoding.UTF8.GetString(exactBytes));
        var exactArgs = new[] { "ruling", "record", "--id", candidate.Id, "--domain", candidate.Domain, "--team", candidate.Team, "--from-file", exactInput, "--authority-role", "operator", "--write", "--format", "json" };
        var writtenExact = InvokeRecordAt(CreateContext(workspace.Root), exactArgs, T0.AddMinutes(2));
        Assert.Equal(0, writtenExact.ExitCode);
        using (var writeResult = JsonDocument.Parse(writtenExact.Output))
        {
            Assert.Equal("written", writeResult.RootElement.GetProperty("disposition").GetString());
            Assert.True(writeResult.RootElement.GetProperty("wrote").GetBoolean());
            Assert.Equal(RulingArtifact.MaximumBytes, File.ReadAllBytes(Path.Combine(targetScope, candidate.Id + ".json")).Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(exactBytes)).ToLowerInvariant(), writeResult.RootElement.GetProperty("content_sha256").GetString());
        }
        foreach (var operation in new[] { "show", "validate" })
        {
            var read = InvokeReadAt(CreateContext(workspace.Root), operation,
                ["ruling", operation, candidate.Id, "--domain", candidate.Domain, "--team", candidate.Team, "--format", "json"], T0.AddMinutes(2));
            Assert.Equal(0, read.ExitCode);
            using var readResult = JsonDocument.Parse(read.Output);
            Assert.Equal("active", readResult.RootElement.GetProperty("status").GetString());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(exactBytes)).ToLowerInvariant(), readResult.RootElement.GetProperty("content_sha256").GetString());
        }

        var overCandidate = candidate with { Id = "R-OTHER-1MIB", Decision = candidate.Decision + "x" };
        var overCanonical = RulingArtifact.Serialize(overCandidate);
        Assert.Equal(RulingArtifact.MaximumBytes + 1, overCanonical.Length);
        var escapedText = Encoding.UTF8.GetString(overCanonical);
        const string unicodeEscape = "\\u0080";
        var escapeOffset = escapedText.IndexOf(unicodeEscape, StringComparison.Ordinal);
        Assert.True(escapeOffset >= 0);
        var rawOverText = escapedText[..escapeOffset] + '\u0080' + escapedText[(escapeOffset + unicodeEscape.Length)..];
        var rawOver = Encoding.UTF8.GetBytes(rawOverText);
        Assert.True(rawOver.Length < RulingArtifact.MaximumBytes);
        Assert.True(RulingArtifact.TryParse(rawOver, false, out var parsedOver, out var rawOverCause, out var rawOverDetail), $"{rawOverCause}: {rawOverDetail}");
        Assert.Equal(RulingArtifact.MaximumBytes + 1, RulingArtifact.Serialize(parsedOver!).Length);
        var overInput = workspace.Write("normalized-over-one-mib.json", rawOverText);
        var overArgs = new[] { "ruling", "record", "--id", overCandidate.Id, "--domain", overCandidate.Domain, "--team", overCandidate.Team, "--from-file", overInput, "--authority-role", "operator", "--write", "--format", "json" };
        var refusedOver = InvokeRecordAt(CreateContext(workspace.Root), overArgs, T0.AddMinutes(2));
        Assert.Equal(1, refusedOver.ExitCode);
        using var overResult = JsonDocument.Parse(refusedOver.Output);
        Assert.Equal("ruling-normalized-size-limit", overResult.RootElement.GetProperty("cause").GetString());
        Assert.False(overResult.RootElement.GetProperty("wrote").GetBoolean());
        Assert.False(File.Exists(Path.Combine(targetScope, overCandidate.Id + ".json")));
    }

    [Fact]
    public void ScopeEvaluation_IsDeterministicAcrossReverseFileCreationOrder()
    {
        using var forward = new TemporaryWorkspace();
        using var reverse = new TemporaryWorkspace();
        var ids = new[] { "R-A", "R-M", "R-Z" };
        foreach (var id in ids)
            WriteArtifact(new RulingArtifactStore(forward.Root), Artifact(id, T0, []));
        foreach (var id in ids.Reverse())
            WriteArtifact(new RulingArtifactStore(reverse.Root), Artifact(id, T0, []));

        var first = new RulingArtifactStore(forward.Root).Evaluate("intent-cli", "intent-cli-dev", "R-M", T0);
        var second = new RulingArtifactStore(reverse.Root).Evaluate("intent-cli", "intent-cli-dev", "R-M", T0);
        Assert.Equal("active", first.Status);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.SupersededBy, second.SupersededBy);
        Assert.Equal(first.ReplacementIds, second.ReplacementIds);
        Assert.Equal(first.Records.Select(record => record.Id), second.Records.Select(record => record.Id));
        Assert.Equal(first.Records.Select(record => Convert.ToHexString(RulingArtifact.Serialize(record))), second.Records.Select(record => Convert.ToHexString(RulingArtifact.Serialize(record))));
    }

    [Fact]
    public void ConcurrentAtomicCreates_DistinguishIdenticalAndDifferentSuppliedBytes()
    {
        var identical = RunRacedCreate(different: false);
        Assert.Equal(1, identical.Count(x => x.Success && x.Wrote));
        Assert.Equal(1, identical.Count(x => x.Success && x.Idempotent));
        Assert.Equal(2, identical.Count(x => x.Success));

        var different = RunRacedCreate(different: true);
        Assert.Equal(1, different.Count(x => x.Success && x.Wrote));
        Assert.Equal(1, different.Count(x => !x.Success && x.Cause == "ruling-content-conflict"));
    }

    [Fact]
    public void TempWriteAndReadbackFaults_KeepPhaseSpecificSideEffects()
    {
        using var tempFailureWorkspace = new TemporaryWorkspace();
        var record = Artifact("R-TEMP-FAULT", T0, []);
        var tempReached = false;
        var tempFault = new RulingArtifactStore(tempFailureWorkspace.Root, (operation, _) =>
        {
            if (operation == "temp-write") { tempReached = true; throw new IOException("temp write sentinel"); }
        });
        Assert.False(tempFault.TryWrite(record, RulingArtifact.Serialize(record), T0, out var tempWrote, out _, out var tempCause, out _, out _));
        Assert.True(tempReached);
        Assert.False(tempWrote);
        Assert.Equal("ruling-write-failed", tempCause);
        var tempScope = Path.Combine(tempFailureWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
        Assert.Empty(Directory.GetFiles(tempScope));

        using var readbackWorkspace = new TemporaryWorkspace();
        var readbackRecord = Artifact("R-READBACK-FAULT", T0, []);
        var readbackReached = false;
        var readbackFault = new RulingArtifactStore(readbackWorkspace.Root, (operation, _) =>
        {
            if (operation == "readback") { readbackReached = true; throw new IOException("readback sentinel"); }
        });
        Assert.False(readbackFault.TryWrite(readbackRecord, RulingArtifact.Serialize(readbackRecord), T0, out var didWrite, out _, out var readbackCause, out _, out _));
        Assert.True(readbackReached);
        Assert.True(didWrite);
        Assert.Equal("ruling-readback-failed", readbackCause);
        Assert.True(File.Exists(Path.Combine(readbackWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-READBACK-FAULT.json")));
    }

    [Fact]
    public void AtomicHardLinkRenameExceptionsFailClosedAtTheReachedWriterBoundary()
    {
        foreach (var (name, failure) in new (string Name, Exception Failure)[]
        {
            ("platform", new PlatformNotSupportedException("binding sentinel")),
            ("library", new DllNotFoundException("binding sentinel")),
            ("entrypoint", new EntryPointNotFoundException("binding sentinel")),
        })
        {
            using var workspace = new TemporaryWorkspace();
            var artifact = Artifact("R-NATIVE-" + name.ToUpperInvariant(), T0, []);
            var reached = false;
            var store = new RulingArtifactStore(workspace.Root, (operation, path) =>
            {
                if (operation == "rename")
                {
                    reached = true;
                    Assert.EndsWith(artifact.Id + ".json", path, StringComparison.Ordinal);
                    throw failure;
                }
            });

            var success = store.TryWrite(artifact, RulingArtifact.Serialize(artifact), T0,
                out var wrote, out var idempotent, out var cause, out _, out _);

            Assert.True(reached);
            Assert.False(success);
            Assert.False(wrote);
            Assert.False(idempotent);
            Assert.Equal("ruling-atomic-create-unavailable", cause);
            var scope = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
            Assert.Empty(Directory.GetFiles(scope));
        }
    }

    [Fact]
    public void AtomicNativeErrorClassifier_SeparatesUnsupportedFromPermissionAndOtherFailures()
    {
        var cases = new (RulingNativePlatform Platform, int Error, string Cause)[]
        {
            (RulingNativePlatform.Linux, 38, "ruling-atomic-create-unavailable"),
            (RulingNativePlatform.Linux, 95, "ruling-atomic-create-unavailable"),
            (RulingNativePlatform.MacOS, 78, "ruling-atomic-create-unavailable"),
            (RulingNativePlatform.MacOS, 45, "ruling-atomic-create-unavailable"),
            (RulingNativePlatform.Windows, 1, "ruling-atomic-create-unavailable"),
            (RulingNativePlatform.Windows, 50, "ruling-atomic-create-unavailable"),
            (RulingNativePlatform.Windows, 120, "ruling-atomic-create-unavailable"),
            (RulingNativePlatform.Linux, 1, "ruling-write-failed"),
            (RulingNativePlatform.Linux, 13, "ruling-write-failed"),
            (RulingNativePlatform.Linux, 18, "ruling-write-failed"),
            (RulingNativePlatform.Linux, 31, "ruling-write-failed"),
            (RulingNativePlatform.MacOS, 1, "ruling-write-failed"),
            (RulingNativePlatform.MacOS, 13, "ruling-write-failed"),
            (RulingNativePlatform.MacOS, 18, "ruling-write-failed"),
            (RulingNativePlatform.MacOS, 31, "ruling-write-failed"),
            (RulingNativePlatform.Windows, 5, "ruling-write-failed"),
        };

        foreach (var (platform, error, expected) in cases)
            Assert.Equal(expected, RulingArtifactStore.ClassifyNativeAtomicCreateError(platform, error));
    }

    [Fact]
    public void AtomicLinkCleanupFailure_RetainsPublishedBytesAndOnlyOwnedTemporaryFile()
    {
        using var workspace = new TemporaryWorkspace();
        var artifact = Artifact("R-CLEANUP-FAULT", T0, []);
        var canonical = RulingArtifact.Serialize(artifact);
        string? ownedTempPath = null;
        var reached = false;
        var store = new RulingArtifactStore(workspace.Root, (operation, path) =>
        {
            if (operation == "owned-temp-cleanup")
            {
                reached = true;
                ownedTempPath = path;
                throw new IOException("post-link cleanup sentinel");
            }
        });

        var success = store.TryWrite(artifact, canonical, T0, out var wrote, out var idempotent,
            out var cause, out var detail, out var created);

        Assert.True(reached);
        Assert.False(success);
        Assert.True(wrote);
        Assert.False(idempotent);
        Assert.Equal("ruling-temp-cleanup-failed", cause);
        Assert.Contains("post-link cleanup sentinel", detail, StringComparison.Ordinal);
        Assert.Equal(new[] { ".intent-cli", ".intent-cli/rulings", ".intent-cli/rulings/intent-cli", ".intent-cli/rulings/intent-cli/intent-cli-dev" }, created);
        var scope = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
        var final = Path.Combine(scope, artifact.Id + ".json");
        Assert.Equal(canonical, File.ReadAllBytes(final));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant(), artifact.Sha256);
        Assert.NotNull(ownedTempPath);
        Assert.Equal(Path.Combine(scope, Path.GetFileName(ownedTempPath)), ownedTempPath);
        Assert.True(File.Exists(ownedTempPath));
        Assert.Equal(new[] { final, ownedTempPath }.Order(StringComparer.Ordinal), Directory.GetFiles(scope).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OwnedTempCleanupFailure_DoesNotReplaceAnEarlierStructuredConflict()
    {
        using var workspace = new TemporaryWorkspace();
        var candidate = Artifact("R-CLEANUP-ON-CONFLICT", T0, ["R-MISSING"]);
        var reached = false;
        var store = new RulingArtifactStore(workspace.Root, (operation, path) =>
        {
            if (operation == "owned-temp-cleanup" && path.Contains(candidate.Id, StringComparison.Ordinal))
            {
                reached = true;
                throw new IOException("cleanup after known graph conflict");
            }
        });

        var success = store.TryWrite(candidate, RulingArtifact.Serialize(candidate), T0,
            out var wrote, out _, out var cause, out var detail, out _);

        Assert.True(reached);
        Assert.False(success);
        Assert.False(wrote);
        Assert.Equal("ruling-supersession-conflict", cause);
        Assert.Contains("missing predecessor", detail, StringComparison.Ordinal);
        Assert.Contains("cleanup after known graph conflict", detail, StringComparison.Ordinal);
        var scope = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
        Assert.Empty(Directory.GetFiles(scope, candidate.Id + ".json"));
        Assert.Single(Directory.GetFiles(scope, "." + candidate.Id + ".*.tmp"));
    }

    [Fact]
    public void NoncanonicalExactScopeBlocksWholeEvaluationButForeignScopeIsNotRead()
    {
        using var workspace = new TemporaryWorkspace();
        var store = new RulingArtifactStore(workspace.Root);
        WriteArtifact(store, Artifact("R-CANON", T0, []));
        var path = Path.Combine(workspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-CANON.json");
        File.WriteAllText(path, Encoding.UTF8.GetString(File.ReadAllBytes(path)).Replace("\n", "\r\n", StringComparison.Ordinal), new UTF8Encoding(false));
        var bad = store.Evaluate("intent-cli", "intent-cli-dev", "R-CANON", T0.AddMinutes(1));
        Assert.Equal("unavailable", bad.Status);
        Assert.Equal("ruling-noncanonical-artifact", bad.Cause);
        Assert.Equal(Path.GetRelativePath(workspace.Root, path).Replace('\\', '/'), Assert.Single(bad.Diagnostics).Path);
        var commandRead = Invoke(CreateContext(workspace.Root), ["ruling", "show", "R-CANON", "--domain", "intent-cli", "--team", "intent-cli-dev", "--format", "json"]);
        Assert.Equal(1, commandRead.ExitCode);
        using (var commandJson = JsonDocument.Parse(commandRead.Output))
        {
            Assert.Equal("ruling-noncanonical-artifact", commandJson.RootElement.GetProperty("cause").GetString());
            Assert.Equal(".intent-cli/rulings/intent-cli/intent-cli-dev/R-CANON.json", commandJson.RootElement.GetProperty("diagnostics")[0].GetProperty("path").GetString());
            Assert.DoesNotContain(workspace.Root, commandRead.Output, StringComparison.Ordinal);
        }

        foreach (var mutation in new[] { "key-order", "bom" })
        {
            using var requestedWorkspace = new TemporaryWorkspace();
            var requestedStore = new RulingArtifactStore(requestedWorkspace.Root);
            WriteArtifact(requestedStore, Artifact("R-REQUESTED", T0, []));
            var requestedPath = Path.Combine(requestedWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-REQUESTED.json");
            var canonical = File.ReadAllBytes(requestedPath);
            var changed = mutation switch
            {
                "key-order" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Replace("  \"schema_version\": \"1\",\n  \"id\": \"R-REQUESTED\",", "  \"id\": \"R-REQUESTED\",\n  \"schema_version\": \"1\",", StringComparison.Ordinal)),
                "bom" => new byte[] { 0xef, 0xbb, 0xbf }.Concat(canonical).ToArray(),
                _ => throw new InvalidOperationException(mutation),
            };
            File.WriteAllBytes(requestedPath, changed);
            var unavailable = requestedStore.Evaluate("intent-cli", "intent-cli-dev", "R-REQUESTED", T0.AddMinutes(1));
            Assert.Equal("unavailable", unavailable.Status);
            Assert.Equal("ruling-noncanonical-artifact", unavailable.Cause);
            Assert.Contains(unavailable.Diagnostics, diagnostic => diagnostic.Path == Path.GetRelativePath(requestedWorkspace.Root, requestedPath).Replace('\\', '/'));
            Assert.Equal(changed, File.ReadAllBytes(requestedPath));
        }

        using (var malformedWorkspace = new TemporaryWorkspace())
        {
            var malformedScope = Path.Combine(malformedWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
            Directory.CreateDirectory(malformedScope);
            var malformedPath = Path.Combine(malformedScope, "R-MALFORMED.json");
            File.WriteAllText(malformedPath, "{ broken", new UTF8Encoding(false));
            var malformed = new RulingArtifactStore(malformedWorkspace.Root).Evaluate("intent-cli", "intent-cli-dev", "R-OTHER", T0);
            Assert.Equal("unavailable", malformed.Status);
            Assert.Equal("ruling-invalid-json", malformed.Cause);
            Assert.Contains(malformed.Diagnostics, diagnostic => diagnostic.Path == Path.GetRelativePath(malformedWorkspace.Root, malformedPath).Replace('\\', '/'));
        }

        using var foreignWorkspace = new TemporaryWorkspace();
        var foreign = Path.Combine(foreignWorkspace.Root, ".intent-cli", "rulings", "other-domain", "other-team");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "malformed.json"), "{ broken", new UTF8Encoding(false));
        var foreignResult = new RulingArtifactStore(foreignWorkspace.Root).Evaluate("intent-cli", "intent-cli-dev", "R-NONE", T0);
        Assert.Equal("missing", foreignResult.Status);
        Assert.Equal("ruling-not-found", foreignResult.Cause);

        foreach (var mutation in new[] { "crlf", "key-order", "bom" })
        {
            using var nonrequestedWorkspace = new TemporaryWorkspace();
            var nonrequestedStore = new RulingArtifactStore(nonrequestedWorkspace.Root);
            WriteArtifact(nonrequestedStore, Artifact("R-REQUESTED", T0, []));
            WriteArtifact(nonrequestedStore, Artifact("R-NONREQUESTED", T0, []));
            var nonrequestedPath = Path.Combine(nonrequestedWorkspace.Root, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", "R-NONREQUESTED.json");
            var original = File.ReadAllBytes(nonrequestedPath);
            var changed = mutation switch
            {
                "crlf" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("\n", "\r\n", StringComparison.Ordinal)),
                "key-order" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("  \"schema_version\": \"1\",\n  \"id\": \"R-NONREQUESTED\",", "  \"id\": \"R-NONREQUESTED\",\n  \"schema_version\": \"1\",", StringComparison.Ordinal)),
                "bom" => new byte[] { 0xef, 0xbb, 0xbf }.Concat(original).ToArray(),
                _ => throw new InvalidOperationException(mutation),
            };
            File.WriteAllBytes(nonrequestedPath, changed);
            var unavailable = nonrequestedStore.Evaluate("intent-cli", "intent-cli-dev", "R-REQUESTED", T0.AddMinutes(1));
            Assert.Equal("unavailable", unavailable.Status);
            Assert.Equal("ruling-noncanonical-artifact", unavailable.Cause);
            Assert.Contains(unavailable.Diagnostics, diagnostic => diagnostic.Path == Path.GetRelativePath(nonrequestedWorkspace.Root, nonrequestedPath).Replace('\\', '/'));
            Assert.Equal(changed, File.ReadAllBytes(nonrequestedPath));
        }
    }

    private static string LocateRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException($"Could not locate repository root from {AppContext.BaseDirectory}.");
    }

    private static string ExtractJsonFence(string markdown, string precedingText)
    {
        var heading = markdown.IndexOf(precedingText, StringComparison.Ordinal);
        Assert.True(heading >= 0, $"Could not find documentation marker '{precedingText}'.");
        var fence = markdown.IndexOf("```json", heading, StringComparison.Ordinal);
        Assert.True(fence >= 0, $"Could not find JSON example after '{precedingText}'.");
        var contentStart = markdown.IndexOf('\n', fence) + 1;
        var contentEnd = markdown.IndexOf("\n```", contentStart, StringComparison.Ordinal);
        Assert.True(contentEnd >= contentStart, $"Could not find the closing JSON fence after '{precedingText}'.");
        return markdown[contentStart..(contentEnd + 1)];
    }

    private static JsonDocument StructuredJsonFromMarkdown(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string marker = "Structured result (same facts as JSON):\n```json\n";
        var start = normalized.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Markdown result did not include its structured JSON facts.");
        start += marker.Length;
        var end = normalized.IndexOf("\n```", start, StringComparison.Ordinal);
        Assert.True(end >= start, "Markdown structured JSON fence was not closed.");
        return JsonDocument.Parse(normalized[start..end]);
    }

    private static void WriteArtifact(RulingArtifactStore store, RulingArtifact artifact)
    {
        Assert.True(store.TryWrite(artifact, RulingArtifact.Serialize(artifact), artifact.RecordedAt, out var wrote, out _, out var cause, out var detail, out _), $"{cause}: {detail}");
        Assert.True(wrote);
    }

    private static RulingArtifact Artifact(string id, DateTimeOffset recordedAt, IReadOnlyList<string> supersedes) => new()
    {
        Id = id, Domain = "intent-cli", Team = "intent-cli-dev", AuthorityRole = "operator", TargetRepo = "J-Tech-Japan/intent-system",
        ScopeKind = "execution-units", ExecutionUnits = ["G862"], Decision = "Keep the bounded choice.", Rationale = "The evidence supports this scope.",
        EvidenceRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/1885"], RecordedAt = recordedAt, ExpiresAt = null,
        Supersedes = supersedes.Order(StringComparer.Ordinal).ToArray(),
    };

    private static string RecordJson(string id, string recordedAt, string executionUnits = "[\"G862\"]", string decision = "\"Keep the bounded choice.\"", string rationale = "\"The evidence supports this scope.\"", string evidence = "\"https://github.com/J-Tech-Japan/intent-system/issues/1885\"") =>
        $$"""
        {
          "schema_version": "1",
          "id": "{{id}}",
          "domain": "intent-cli",
          "team": "intent-cli-dev",
          "authority_role": "operator",
          "scope": { "target_repo": "J-Tech-Japan/intent-system", "kind": "execution-units", "execution_units": {{executionUnits}} },
          "decision": {{decision}},
          "rationale": {{rationale}},
          "evidence_refs": [{{evidence}}],
          "recorded_at": "{{recordedAt}}",
          "expires_at": null,
          "supersedes": []
        }
        """.Replace("\n", "\n", StringComparison.Ordinal);

    private static (int ExitCode, string Output) Invoke(CliContext context, string[] args)
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(args, context, writer);
        return (exit, writer.ToString());
    }

    private static (int ExitCode, string Output) InvokeRecordAt(CliContext context, string[] args, DateTimeOffset now)
    {
        using var writer = new StringWriter();
        var exit = RulingCommand.ExecuteRecord(context, args.Skip(2).ToArray(), writer, beforeOperation: null, nowOverride: now);
        return (exit, writer.ToString());
    }

    private static (int ExitCode, string Output) InvokeReadAt(CliContext context, string operation, string[] args, DateTimeOffset now)
    {
        using var writer = new StringWriter();
        var exit = operation switch
        {
            "show" => RulingCommand.ExecuteShow(context, args.Skip(2).ToArray(), writer, now),
            "validate" => RulingCommand.ExecuteValidate(context, args.Skip(2).ToArray(), writer, now),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Expected show or validate."),
        };
        return (exit, writer.ToString());
    }

    private static CliContext CreateContext(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" } },
    };

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            var tempRoot = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
            Root = Path.Combine(tempRoot, "intent-cli-ruling-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        public string Root { get; }
        public string CreateChild(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }
        public string Write(string name, string text)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
        public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch (IOException) { } }
    }

    private static void CopyScope(string fromRoot, string toRoot)
    {
        var source = Path.Combine(fromRoot, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
        var target = Path.Combine(toRoot, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }

    private static void CopyArtifact(string fromRoot, string toRoot, string id)
    {
        var source = Path.Combine(fromRoot, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", id + ".json");
        var target = Path.Combine(toRoot, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", id + ".json");
        File.Copy(source, target);
    }

    private sealed record RulingRemote(string BarePath, string SeedClone);

    private static RulingRemote CreateBareRulingRemote(TemporaryWorkspace workspace, string name, IReadOnlyList<RulingArtifact> seedRecords)
    {
        var bare = workspace.CreateChild(name + "-origin.git");
        var seed = workspace.CreateChild(name + "-seed");
        RunGit(workspace.Root, "init", "--bare", "--initial-branch=main", bare);
        RunGit(workspace.Root, "clone", bare, seed);
        ConfigureGitClone(seed);
        File.WriteAllText(Path.Combine(seed, "README.md"), "G862 immutable baseline\n", new UTF8Encoding(false));
        foreach (var record in seedRecords) WriteActualRuling(workspace, seed, record, record.RecordedAt);
        RunGit(seed, "add", "--all");
        RunGit(seed, "commit", "-m", "seed actual ruling artifacts");
        RunGit(seed, "push", "origin", "HEAD:refs/heads/main");
        return new RulingRemote(bare, seed);
    }

    private static string CloneRulingRemote(TemporaryWorkspace workspace, RulingRemote remote, string name)
    {
        var clone = workspace.CreateChild(name);
        RunGit(workspace.Root, "clone", remote.BarePath, clone);
        ConfigureGitClone(clone);
        return clone;
    }

    private static void ConfigureGitClone(string clone)
    {
        RunGit(clone, "config", "user.name", "G862 test operator");
        RunGit(clone, "config", "user.email", "g862-test@example.invalid");
    }

    private static (int ExitCode, string Output) InvokeActualRuling(TemporaryWorkspace workspace, string repoRoot, RulingArtifact record, DateTimeOffset now)
    {
        var inputPath = workspace.Write("candidate-" + Guid.NewGuid().ToString("N") + ".json", Encoding.UTF8.GetString(RulingArtifact.Serialize(record)));
        var args = new[]
        {
            "ruling", "record", "--id", record.Id, "--domain", record.Domain, "--team", record.Team,
            "--from-file", inputPath, "--authority-role", "operator", "--write", "--format", "json",
        };
        return InvokeRecordAt(CreateContext(repoRoot), args, now);
    }

    private static void WriteActualRuling(TemporaryWorkspace workspace, string repoRoot, RulingArtifact record, DateTimeOffset now)
    {
        var result = InvokeActualRuling(workspace, repoRoot, record, now);
        Assert.True(result.ExitCode == 0, result.Output);
        using var output = JsonDocument.Parse(result.Output);
        Assert.Equal("written", output.RootElement.GetProperty("disposition").GetString());
        Assert.True(output.RootElement.GetProperty("wrote").GetBoolean());
        Assert.Equal(RulingArtifact.Serialize(record), ReadRulingBytes(repoRoot, record.Id));
    }

    private static string RulingPath(string repoRoot, string id) => Path.Combine(repoRoot, ".intent-cli", "rulings", "intent-cli", "intent-cli-dev", id + ".json");

    private static byte[] ReadRulingBytes(string repoRoot, string id) => File.ReadAllBytes(RulingPath(repoRoot, id));

    private static void CommitAndPushCurrentBranch(string clone, string branch, string message)
    {
        RunGit(clone, "add", "--all");
        RunGit(clone, "commit", "-m", message);
        RunGit(clone, "push", "origin", "HEAD:refs/heads/" + branch);
    }

    private static void MergePublishedBranches(string aggregate, IEnumerable<string> branches)
    {
        foreach (var branch in branches)
        {
            var remoteBranch = "refs/remotes/origin/" + branch;
            RunGit(aggregate, "fetch", "origin", "refs/heads/" + branch + ":" + remoteBranch);
            RunGit(aggregate, "merge", "--no-edit", remoteBranch);
        }
    }

    private static List<(bool Success, bool Wrote, bool Idempotent, string Cause)> RunRacedCreate(bool different)
    {
        using var workspace = new TemporaryWorkspace();
        using var barrier = new Barrier(2);
        var first = Artifact("R-RACE", T0, []) with { Decision = "same bytes" };
        var second = different ? first with { Decision = "different bytes" } : first;
        Task<(bool Success, bool Wrote, bool Idempotent, string Cause)> Start(RulingArtifact artifact) => Task.Run(() =>
        {
            var store = new RulingArtifactStore(workspace.Root, (operation, _) =>
            {
                if (operation == "rename" && !barrier.SignalAndWait(TimeSpan.FromSeconds(15)))
                    throw new IOException("raced create barrier timeout");
            });
            var success = store.TryWrite(artifact, RulingArtifact.Serialize(artifact), T0, out var wrote, out var idempotent, out var cause, out _, out _);
            return (success, wrote, idempotent, cause);
        });
        return Task.WhenAll(Start(first), Start(second)).GetAwaiter().GetResult().ToList();
    }

    private static string RunGit(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git process did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("git test command exceeded its bounded timeout"); }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr.Result}");
        return stdout.Result;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string pathname, int mode);
}
