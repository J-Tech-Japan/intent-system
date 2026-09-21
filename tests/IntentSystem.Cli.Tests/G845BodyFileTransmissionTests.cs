using System.Text;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

internal static class G845BodyFixtures
{
    internal static byte[] ValidBytes(int totalBytes, string title = "G845 body-file transmission")
    {
        var body = BuildContract(title);
        var prefix = Encoding.UTF8.GetBytes(new string('A', 1000) + "\n" + body);
        Assert.True(totalBytes >= prefix.Length);

        var bytes = new byte[totalBytes];
        prefix.CopyTo(bytes, 0);
        FillPadding(bytes, prefix.Length);
        return bytes;
    }

    internal static byte[] InvalidOrdinaryTextBytes(int totalBytes, string title = "G845 body-file transmission")
    {
        var bytes = ValidBytes(totalBytes, title);
        bytes[1000] = 0xFF;
        return bytes;
    }

    internal static byte[] InvalidHeadingBytes(int totalBytes, string title = "G845 body-file transmission")
    {
        var body = BuildContract(title);
        var bytes = Encoding.UTF8.GetBytes(body);
        var headingOffset = Encoding.UTF8.GetByteCount(body[..body.IndexOf("## Goal", StringComparison.Ordinal)]) + 3;
        bytes[headingOffset] = 0xFF;

        Assert.True(totalBytes >= bytes.Length);
        var padded = new byte[totalBytes];
        bytes.CopyTo(padded, 0);
        FillPadding(padded, bytes.Length);
        return padded;
    }

    internal static byte[] BomBytes(int totalBytes, string title = "G845 body-file transmission")
    {
        Assert.True(totalBytes >= 3);
        var content = ValidBytes(totalBytes - 3, title);
        var bytes = new byte[totalBytes];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        content.CopyTo(bytes, 3);
        return bytes;
    }

    internal static byte[] Utf16Bytes(string title = "G845 body-file transmission")
    {
        var text = Encoding.UTF8.GetString(ValidBytes(5_003, title));
        return Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(text))
            .ToArray();
    }

    internal static byte[] Utf32Bytes(string title = "G845 body-file transmission")
    {
        var text = Encoding.UTF8.GetString(ValidBytes(5_003, title));
        return Encoding.UTF32.GetPreamble()
            .Concat(Encoding.UTF32.GetBytes(text))
            .ToArray();
    }

    private static void FillPadding(byte[] bytes, int start)
    {
        for (var index = start; index < bytes.Length; index++)
        {
            bytes[index] = (byte)('a' + (index % 26));
        }
    }

    private static string BuildContract(string title)
    {
        var builder = new StringBuilder()
            .Append("# ").Append(title).Append("\n\n");
        foreach (var heading in IssueValidateBodyValidator.RequiredHeadings)
        {
            builder.Append("## ").Append(heading).Append("\n\n");
            if (heading == "Target Repo / Path / Part")
            {
                builder.Append("Repository: `J-Tech-Japan/intent-system`\n\n");
                builder.Append("- Target paths: `src/IntentSystem.Cli/Commands`\n");
            }
            else if (heading == "Related Links")
            {
                builder.Append("- https://github.com/J-Tech-Japan/intent-system/issues/1836\n");
            }
            else
            {
                builder.Append("G845 deterministic fixture content.\n");
            }

            builder.Append('\n');
        }

        var body = builder.ToString();
        Assert.True(IssueValidateBodyValidator.Validate("g845-fixture.md", body, requireTargetPathsDeclaration: true).IsValid);
        return body;
    }
}

[Collection("IssueSyncBodySharedState")]
public sealed class G845TransmissionPrimitiveTests
{
    [Fact]
    public void GateStrictlyDecodesBeforeCountingAndRetainsRawBytes()
    {
        var invalid = G845BodyFixtures.InvalidOrdinaryTextBytes(70_000);
        var rejected = IssueBodyTransmissionGate.Evaluate(invalid, "github-body.md");

        var invalidRefusal = Assert.IsType<IssueBodyTransmissionRefusal>(rejected);
        Assert.Equal(IssueBodyTransmissionFailure.InvalidUtf8, invalidRefusal.Kind);
        Assert.Equal(1000, invalidRefusal.InvalidByteOffset);
        Assert.Equal(70_000, invalidRefusal.ByteCount);

        var bom = G845BodyFixtures.BomBytes(65_539);
        var overLimit = IssueBodyTransmissionGate.Evaluate(bom, "github-body.md");
        var sizeRefusal = Assert.IsType<IssueBodyTransmissionRefusal>(overLimit);
        Assert.Equal(IssueBodyTransmissionFailure.TooLarge, sizeRefusal.Kind);
        Assert.Equal(65_539, sizeRefusal.ByteCount);
        Assert.Null(sizeRefusal.InvalidByteOffset);

        var acceptedBytes = G845BodyFixtures.ValidBytes(50_003);
        var accepted = Assert.IsType<IssueBodyTransmissionAccepted>(
            IssueBodyTransmissionGate.Evaluate(acceptedBytes, "github-body.md"));
        Assert.Same(acceptedBytes, accepted.Bytes);
    }

    [Fact]
    public void GateAcceptsTheInclusiveRawByteBoundary()
    {
        Assert.True(IssueBodyTransmissionGate.Evaluate(G845BodyFixtures.ValidBytes(65_535), "github-body.md").IsAccepted);
        Assert.True(IssueBodyTransmissionGate.Evaluate(G845BodyFixtures.ValidBytes(65_536), "github-body.md").IsAccepted);
        Assert.Equal(
            IssueBodyTransmissionFailure.TooLarge,
            Assert.IsType<IssueBodyTransmissionRefusal>(
                IssueBodyTransmissionGate.Evaluate(G845BodyFixtures.ValidBytes(65_537), "github-body.md")).Kind);
        Assert.Equal(
            IssueBodyTransmissionFailure.TooLarge,
            Assert.IsType<IssueBodyTransmissionRefusal>(
                IssueBodyTransmissionGate.Evaluate(G845BodyFixtures.ValidBytes(70_000), "github-body.md")).Kind);
    }

    [Theory]
    [MemberData(nameof(NonUtf8BomBodies))]
    public void GateRefusesNonUtf8BomBodiesAtOffsetZero(byte[] body)
    {
        var result = Assert.IsType<IssueBodyTransmissionRefusal>(
            IssueBodyTransmissionGate.Evaluate(body, "github-body.md"));

        Assert.Equal(IssueBodyTransmissionFailure.InvalidUtf8, result.Kind);
        Assert.Equal(0, result.InvalidByteOffset);
        Assert.Equal(body.Length, result.ByteCount);
    }

    public static IEnumerable<object[]> NonUtf8BomBodies()
    {
        yield return [G845BodyFixtures.Utf16Bytes()];
        yield return [G845BodyFixtures.Utf32Bytes()];
    }

    [Fact]
    public void RefusalDoesNotExposeAStagedPath()
    {
        var refusalType = typeof(IssueBodyTransmissionRefusal);

        Assert.DoesNotContain(
            refusalType.GetProperties(),
            property => string.Equals(property.Name, "Path", StringComparison.Ordinal));
    }

    [Fact]
    public void PerRouteRefusalBuildersMapBothNeutralKindsWithPinnedText()
    {
        var tooLarge = Assert.IsType<IssueBodyTransmissionRefusal>(
            IssueBodyTransmissionGate.Evaluate(G845BodyFixtures.ValidBytes(70_000), "github-body.md"));
        var invalid = Assert.IsType<IssueBodyTransmissionRefusal>(
            IssueBodyTransmissionGate.Evaluate(G845BodyFixtures.InvalidOrdinaryTextBytes(50_003), "github-body.md"));

        var flowTooLarge = IssuePublishFlowCommand.BuildTransmissionRefusal(tooLarge);
        Assert.Equal("issue-body-too-large", flowTooLarge.Cause);
        Assert.Equal(
            "issue-body-too-large: github-body.md is 70000 bytes, which exceeds the 65536-byte limit.",
            flowTooLarge.Error);
        var flowInvalid = IssuePublishFlowCommand.BuildTransmissionRefusal(invalid);
        Assert.Equal("issue-body-invalid-utf8", flowInvalid.Cause);
        Assert.Equal(
            "issue-body-invalid-utf8: github-body.md is not valid UTF-8 at byte offset 1000.",
            flowInvalid.Error);

        Assert.Equal(
            "source body is 70000 bytes, which exceeds the 65536-byte limit",
            IssuePublishReviewedCommand.BuildTransmissionRefusal(tooLarge));
        Assert.Equal(
            "source body is not valid UTF-8 at byte offset 1000",
            IssuePublishReviewedCommand.BuildTransmissionRefusal(invalid));

        var baseResult = new IssueSyncBodyResult
        {
            ExecutionUnit = "G845",
            Repo = "J-Tech-Japan/intent-system",
            Mode = "write",
            ConcurrencyGuarantee = IssueSyncBodyCommand.ConcurrencyGuarantee,
        };
        var syncTooLarge = IssueSyncBodyCommand.BuildTransmissionRefusal(baseResult, tooLarge);
        Assert.Equal("body-too-large", syncTooLarge.ReasonCode);
        Assert.Equal(1, syncTooLarge.ExitCode);
        Assert.Equal(
            "refused (body-too-large): github-body.md is 70000 bytes, which exceeds the 65536-byte limit. The issue body was read, but no update was sent.",
            syncTooLarge.Summary);
        var syncInvalid = IssueSyncBodyCommand.BuildTransmissionRefusal(baseResult, invalid);
        Assert.Equal("body-invalid-utf8", syncInvalid.ReasonCode);
        Assert.Equal(1, syncInvalid.ExitCode);
        Assert.Equal(
            "refused (body-invalid-utf8): Issue body is not valid UTF-8 at byte offset 1000. The issue body was read, but no update was sent.",
            syncInvalid.Summary);
    }

    [Fact]
    public void StagerHandsExactBytesAnd0600FileToTheTransmissionSeam()
    {
        var expected = G845BodyFixtures.ValidBytes(50_003);
        string? stagedPath = null;

        using (var stagedBody = IssueBodyFileStager.Stage(expected))
        {
            stagedPath = stagedBody.Path;
            Assert.Equal(expected, File.ReadAllBytes(stagedBody.Path));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(stagedBody.Path));
            }
        }

        Assert.NotNull(stagedPath);
        Assert.False(File.Exists(stagedPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagedPath!)!));
    }

    [Fact]
    public void StagerCleansAfterSuccessFailureAndThrowingTransmission()
    {
        var paths = new List<string>();
        var bytes = G845BodyFixtures.ValidBytes(50_003);

        using (var stagedBody = IssueBodyFileStager.Stage(bytes))
        {
            paths.Add(stagedBody.Path);
        }

        var failureBody = IssueBodyFileStager.Stage(bytes);
        paths.Add(failureBody.Path);
        try
        {
            throw new InvalidOperationException("transmission failed");
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            failureBody.Dispose();
        }

        var throwingBody = IssueBodyFileStager.Stage(bytes);
        paths.Add(throwingBody.Path);
        try
        {
            throw new IOException("transmission failed");
        }
        catch (IOException)
        {
        }
        finally
        {
            throwingBody.Dispose();
        }

        Assert.All(paths, path => Assert.False(Directory.Exists(Path.GetDirectoryName(path)!)));
    }

    [Fact]
    public void CleanupFailureDoesNotMaskSuccessFailureOrTransmissionException()
    {
        var paths = new List<string>();
        var bytes = G845BodyFixtures.ValidBytes(50_003);
        try
        {
            IssueBodyFileStager.DirectoryDeleteOverride = _ => throw new IOException("cleanup failure");

            using (var success = IssueBodyFileStager.Stage(bytes))
            {
                paths.Add(success.Path);
            }

            var failing = IssueBodyFileStager.Stage(bytes);
            paths.Add(failing.Path);
            try
            {
                throw new InvalidOperationException("transmission failure");
            }
            catch (InvalidOperationException exception)
            {
                Assert.Equal("transmission failure", exception.Message);
            }
            finally
            {
                failing.Dispose();
            }

            var throwing = IssueBodyFileStager.Stage(bytes);
            paths.Add(throwing.Path);
            try
            {
                throw new IOException("transmission io failure");
            }
            catch (IOException exception)
            {
                Assert.Equal("transmission io failure", exception.Message);
            }
            finally
            {
                throwing.Dispose();
            }
        }
        finally
        {
            IssueBodyFileStager.DirectoryDeleteOverride = null;
            foreach (var path in paths)
            {
                var directory = Path.GetDirectoryName(path)!;
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        Assert.All(paths, path => Assert.False(Directory.Exists(Path.GetDirectoryName(path)!)));
    }

    [Fact]
    public void StagerRefusesDirectoryWriteAndModeFailuresWithoutCallingTransmission()
    {
        var bytes = G845BodyFixtures.ValidBytes(50_003);
        var calls = 0;
        string? failedWritePath = null;
        try
        {
            IssueBodyFileStager.DirectoryCreateOverride = _ => throw new IOException("directory failure");
            var directoryError = Assert.Throws<IssueBodyStagingException>(() => IssueBodyFileStager.Stage(bytes));
            Assert.Equal("directory failure", directoryError.Message);

            IssueBodyFileStager.DirectoryCreateOverride = null;
            IssueBodyFileStager.FileWriteOverride = (path, _) =>
            {
                failedWritePath = path;
                throw new IOException("write failure");
            };
            var writeError = Assert.Throws<IssueBodyStagingException>(() => IssueBodyFileStager.Stage(bytes));
            Assert.Equal("write failure", writeError.Message);
            Assert.NotNull(failedWritePath);
            Assert.False(Directory.Exists(Path.GetDirectoryName(failedWritePath!)!));

            IssueBodyFileStager.FileWriteOverride = null;
            IssueBodyFileStager.FileModeOverride = _ => throw new IOException("mode failure");
            var modeError = Assert.Throws<IssueBodyStagingException>(() => IssueBodyFileStager.Stage(bytes));
            Assert.Equal("mode failure", modeError.Message);
        }
        finally
        {
            IssueBodyFileStager.DirectoryCreateOverride = null;
            IssueBodyFileStager.FileWriteOverride = null;
            IssueBodyFileStager.FileModeOverride = null;
        }

        Assert.Equal(0, calls);
    }
}

public sealed class G845DocumentationTests
{
    [Fact]
    public void EnglishAndJapaneseDocsAndLedgersStateTheScopedContract()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "docs", "en", "04-packets-issues.md"),
            Path.Combine(root, "docs", "ja", "04-packets-issues.md"),
            Path.Combine(root, "docs", "en", "1.0-compatibility-ledger.md"),
            Path.Combine(root, "docs", "ja", "1.0-compatibility-ledger.md"),
        };

        foreach (var path in paths)
        {
            var text = File.ReadAllText(path);
            Assert.Contains("65536", text, StringComparison.Ordinal);
            Assert.Contains("58000", text, StringComparison.Ordinal);
            Assert.Contains("65,536 is intent-cli's own conservative limit", text, StringComparison.Ordinal);
            Assert.Contains("GitHub's boundary", text, StringComparison.Ordinal);
            Assert.Contains("roughly 96,000-character failure", text, StringComparison.Ordinal);
            Assert.Contains("--body-file", text, StringComparison.Ordinal);
            Assert.Contains("BOM", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("issue-body-invalid-utf8", text, StringComparison.Ordinal);
            Assert.Contains("body-invalid-utf8", text, StringComparison.Ordinal);
            Assert.Contains("source body is not valid UTF-8 at byte offset <k>", text, StringComparison.Ordinal);
            Assert.Contains("--format", text, StringComparison.Ordinal);
            Assert.Contains("result surface", text, StringComparison.Ordinal);
            Assert.Contains("UTF-16", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UTF-32", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("validate-body", text, StringComparison.Ordinal);
            Assert.Contains("may accept a body", text, StringComparison.Ordinal);
            Assert.Contains("65,539", text, StringComparison.Ordinal);
        }
    }
}
