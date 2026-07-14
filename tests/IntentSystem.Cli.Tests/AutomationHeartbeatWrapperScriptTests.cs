using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G526 semantic-review repair: executable adversarial coverage for the
/// copy-paste heartbeat wrapper script documented in the orchestrator-thread
/// guide's "External heartbeat" section. The wrapper text is extracted from
/// the REAL guide command output (not a hand-duplicated copy), so there is
/// zero drift between what ships in the guide/docs and what is executed
/// here — this test runs the actual script under <c>/bin/sh</c> against
/// fake <c>intent-cli</c>/<c>send.sh</c> stubs, on POSIX platforms only
/// (the wrapper is POSIX sh; Windows tests no-op).
/// </summary>
public sealed class AutomationHeartbeatWrapperScriptTests : IDisposable
{
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly string root;
    private readonly string binDir;
    private readonly string homeDir;
    private readonly string captureDir;
    private readonly string wrapperPath;

    public AutomationHeartbeatWrapperScriptTests()
    {
        root = Directory.CreateTempSubdirectory("heartbeat-wrapper-tests-").FullName;
        binDir = Path.Combine(root, "bin");
        homeDir = Path.Combine(root, "home");
        captureDir = Path.Combine(root, "capture");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(Path.Combine(homeDir, ".agents", "skills", "agmsg", "scripts"));
        Directory.CreateDirectory(captureDir);

        var sendScript = Path.Combine(homeDir, ".agents", "skills", "agmsg", "scripts", "send.sh");
        WriteExecutable(
            sendScript,
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$1\" > \"$CAPTURE_DIR/team\"\n"
            + "printf '%s\\n' \"$2\" > \"$CAPTURE_DIR/from\"\n"
            + "printf '%s\\n' \"$3\" > \"$CAPTURE_DIR/to\"\n"
            + "printf '%s' \"$4\" > \"$CAPTURE_DIR/message\"\n"
            + "echo called >> \"$CAPTURE_DIR/calls.log\"\n");

        wrapperPath = Path.Combine(root, "wrapper.sh");
        WriteExecutable(wrapperPath, ExtractWrapperScript());
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HealthyOutput_ExitsZero_NeverSends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFakeIntentCli("#!/bin/sh\necho '{\"stale\":false,\"items\":[],\"message_body\":null}'\n");

        var (exitCode, _, _) = RunWrapper();

        Assert.Equal(0, exitCode);
        AssertNeverSent();
    }

    [Fact]
    public void StaleWithAdversarialMessageBody_SendsExactlyOnce_ContentPreservedByteForByte()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string adversarial =
            "line1\nline2 with \"quotes\" and \\backslash\\ and `backtick` and $(cmd) and ; and | and & and > and <";
        var payload = JsonSerializer.Serialize(new
        {
            stale = true,
            items = new[] { new { execution_unit = "G1" } },
            message_body = adversarial,
        });
        WriteFakeIntentCli("#!/bin/sh\ncat << 'HEARTBEAT_JSON_EOF'\n" + payload + "\nHEARTBEAT_JSON_EOF\n");

        var (exitCode, _, stderr) = RunWrapper();

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Equal(1, CountSendCalls());
        Assert.Equal("intent-cli-dev", ReadCaptured("team"));
        Assert.Equal("heartbeat", ReadCaptured("from"));
        Assert.Equal("orchestrator", ReadCaptured("to"));
        // No trailing newline on the message capture — proves the exact
        // byte content (including embedded newline, quotes, backslashes,
        // backtick, $(), and shell metacharacters) survived as ONE inert
        // argument with zero shell reinterpretation.
        Assert.Equal(adversarial, File.ReadAllText(Path.Combine(captureDir, "message")));
    }

    [Fact]
    public void IntentCliCommandFails_FailsClosed_NeverSends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFakeIntentCli("#!/bin/sh\necho 'error: gh auth failure' >&2\nexit 1\n");

        var (exitCode, _, stderr) = RunWrapper();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("heartbeat: intent-cli automation heartbeat failed", stderr, StringComparison.Ordinal);
        AssertNeverSent();
    }

    [Fact]
    public void MalformedNonJsonOutput_FailsClosed_NeverSends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFakeIntentCli("#!/bin/sh\necho 'not json at all'\n");

        var (exitCode, _, stderr) = RunWrapper();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("malformed output", stderr, StringComparison.Ordinal);
        AssertNeverSent();
    }

    [Fact]
    public void StaleFieldMissing_FailsClosed_NeverSends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFakeIntentCli("#!/bin/sh\necho '{}'\n");

        var (exitCode, _, stderr) = RunWrapper();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("malformed output", stderr, StringComparison.Ordinal);
        AssertNeverSent();
    }

    [Fact]
    public void StaleFieldNull_FailsClosed_NeverSends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFakeIntentCli("#!/bin/sh\necho '{\"stale\":null}'\n");

        var (exitCode, _, stderr) = RunWrapper();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("malformed output", stderr, StringComparison.Ordinal);
        AssertNeverSent();
    }

    [Fact]
    public void StaleTrueButMessageBodyNull_FailsClosed_NeverSends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFakeIntentCli("#!/bin/sh\necho '{\"stale\":true,\"message_body\":null}'\n");

        var (exitCode, _, stderr) = RunWrapper();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("message_body is missing/empty", stderr, StringComparison.Ordinal);
        AssertNeverSent();
    }

    [Fact]
    public void StaleTrueButMessageBodyEmpty_FailsClosed_NeverSends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFakeIntentCli("#!/bin/sh\necho '{\"stale\":true,\"message_body\":\"\"}'\n");

        var (exitCode, _, stderr) = RunWrapper();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("message_body is missing/empty", stderr, StringComparison.Ordinal);
        AssertNeverSent();
    }

    private static string ExtractWrapperScript()
    {
        using var writer = new StringWriter();
        GuideOrchestratorThreadCommand.Execute(
            new CliContext
            {
                RepoRoot = Path.GetTempPath(),
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            },
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);
        using var doc = JsonDocument.Parse(writer.ToString());
        return doc.RootElement.GetProperty("external_heartbeat").GetProperty("wrapper_example").GetString()!;
    }

    private void WriteFakeIntentCli(string script) => WriteExecutable(Path.Combine(binDir, "intent-cli"), script);

    private static void WriteExecutable(string path, string content)
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, ExecutableMode);
        }
    }

    private (int ExitCode, string Stdout, string Stderr) RunWrapper()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(wrapperPath);
        startInfo.Environment["PATH"] = binDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        startInfo.Environment["HOME"] = homeDir;
        startInfo.Environment["TEAM"] = "intent-cli-dev";
        startInfo.Environment["FROM"] = "heartbeat";
        startInfo.Environment["TO"] = "orchestrator";
        startInfo.Environment["DOMAIN"] = "intent-cli";
        startInfo.Environment["REPO"] = "J-Tech-Japan/intent-system";
        startInfo.Environment["CAPTURE_DIR"] = captureDir;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start /bin/sh for the wrapper script test");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private int CountSendCalls()
    {
        var callsLog = Path.Combine(captureDir, "calls.log");
        return File.Exists(callsLog)
            ? File.ReadAllLines(callsLog).Count(line => !string.IsNullOrWhiteSpace(line))
            : 0;
    }

    private void AssertNeverSent() => Assert.Equal(0, CountSendCalls());

    private string ReadCaptured(string name) => File.ReadAllText(Path.Combine(captureDir, name)).TrimEnd('\n');
}
