using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G700: the only retry policy used for intent-cli-owned host-state Git writes.
/// The policy is deliberately small and explicit so an operator can see the
/// retry bound and time window in the role-facing guides.
/// </summary>
internal sealed record HostStateGitRetryPolicy(
    int MaxAttempts,
    int WindowMilliseconds,
    int InitialDelayMilliseconds,
    int MaxDelayMilliseconds,
    int JitterMilliseconds)
{
    internal const int DefaultMaxAttempts = 4;
    internal const int DefaultWindowMilliseconds = 2000;
    internal const int DefaultInitialDelayMilliseconds = 25;
    internal const int DefaultMaxDelayMilliseconds = 250;
    internal const int DefaultJitterMilliseconds = 25;

    internal static HostStateGitRetryPolicy Default { get; } = new(
        DefaultMaxAttempts,
        DefaultWindowMilliseconds,
        DefaultInitialDelayMilliseconds,
        DefaultMaxDelayMilliseconds,
        DefaultJitterMilliseconds);

    internal TimeSpan Window => TimeSpan.FromMilliseconds(WindowMilliseconds);

    internal TimeSpan InitialDelay => TimeSpan.FromMilliseconds(InitialDelayMilliseconds);

    internal TimeSpan MaxDelay => TimeSpan.FromMilliseconds(MaxDelayMilliseconds);

    internal TimeSpan Jitter => TimeSpan.FromMilliseconds(JitterMilliseconds);

    internal string Describe() =>
        $"max_attempts={MaxAttempts}, window={WindowMilliseconds}ms, "
        + $"initial_delay={InitialDelayMilliseconds}ms, max_delay={MaxDelayMilliseconds}ms, "
        + $"jitter={JitterMilliseconds}ms";

    internal string GuideDescription() =>
        "G700 host-state Git write safety: intent-cli retries only a Git failure "
        + "that identifies .git/index.lock contention, with the declared policy "
        + $"({Describe()}). A successful retry reports git_write_retry.outcome="
        + "succeeded and the actual attempts. Exhaustion reports attempts, elapsed "
        + "milliseconds, the exact lock path, the original Git error, and manual "
        + "remediation. Never delete, rename, move, truncate, or repair index.lock; "
        + "inspect it and confirm no Git process owns it before manual cleanup. "
        + "Non-lock Git failures are not retried.";
}

/// <summary>
/// Retries a single intent-cli-owned Git write only for a recognizable
/// <c>.git/index.lock</c> contention error. It never inspects, deletes, or
/// repairs the lock file; remediation is left to the operator.
/// </summary>
internal static class HostStateGitRetryRunner
{
    private static readonly Regex QuotedLockPath = new(
        "['\"](?<path>[^'\"]*index\\.lock)['\"]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BareLockPath = new(
        "(?<path>(?:[A-Za-z]:)?[^\\s'\"]*index\\.lock)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static ClaimProcessResult Run(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Func<ClaimProcessResult> runOnce,
        HostStateGitRetryPolicy? policy = null,
        Action<TimeSpan>? sleep = null,
        Func<double>? jitterSample = null)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(runOnce);

        policy ??= HostStateGitRetryPolicy.Default;
        Validate(policy);
        sleep ??= static delay => Thread.Sleep(delay);
        jitterSample ??= static () => Random.Shared.NextDouble();

        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        var firstError = string.Empty;
        string? lockPath = null;

        while (true)
        {
            var result = runOnce();
            attempts++;

            if (result.ExitCode == 0)
            {
                if (firstError.Length == 0)
                {
                    return result;
                }

                return result with
                {
                    RetryEvidence = BuildEvidence(
                        "succeeded", attempts, stopwatch.Elapsed, lockPath, firstError,
                        workingDirectory, policy),
                };
            }

            if (!TryGetIndexLockContention(result, workingDirectory, out var detectedPath))
            {
                // A non-lock Git failure is returned exactly once. In
                // particular, a generic Git failure mentioning another file
                // never enters this retry loop.
                return result;
            }

            if (firstError.Length == 0)
            {
                firstError = OriginalGitError(result);
                lockPath = detectedPath;
            }

            var elapsed = stopwatch.Elapsed;
            if (attempts >= policy.MaxAttempts || elapsed >= policy.Window)
            {
                return result with
                {
                    RetryEvidence = BuildEvidence(
                        "exhausted", attempts, elapsed, lockPath, firstError,
                        workingDirectory, policy),
                };
            }

            var delay = DelayForRetry(policy, attempts, jitterSample());
            if (elapsed + delay > policy.Window)
            {
                return result with
                {
                    RetryEvidence = BuildEvidence(
                        "exhausted", attempts, elapsed, lockPath, firstError,
                        workingDirectory, policy),
                };
            }

            sleep(delay);
            if (stopwatch.Elapsed >= policy.Window)
            {
                return result with
                {
                    RetryEvidence = BuildEvidence(
                        "exhausted", attempts, stopwatch.Elapsed, lockPath, firstError,
                        workingDirectory, policy),
                };
            }
        }
    }

    internal static bool TryGetIndexLockContention(
        ClaimProcessResult result,
        string workingDirectory,
        out string lockPath)
    {
        var text = $"{result.StandardError}\n{result.StandardOutput}";
        var hasIndexLock = text.Contains("index.lock", StringComparison.OrdinalIgnoreCase);
        var hasContentionPhrase =
            text.Contains("file exists", StringComparison.OrdinalIgnoreCase)
            || text.Contains("already exists", StringComparison.OrdinalIgnoreCase);

        if (!hasIndexLock || !hasContentionPhrase)
        {
            lockPath = string.Empty;
            return false;
        }

        var match = QuotedLockPath.Match(text);
        if (!match.Success)
        {
            match = BareLockPath.Match(text);
        }

        var rawPath = match.Success
            ? match.Groups["path"].Value
            : Path.Combine(workingDirectory, ".git", "index.lock");
        lockPath = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.GetFullPath(rawPath, workingDirectory);
        return true;
    }

    internal static string TerminalDetail(HostStateGitRetryEvidence evidence) =>
        $"index.lock contention exhausted after {evidence.Attempts} attempt(s) over "
        + $"{evidence.ElapsedMilliseconds}ms. Original git error: {evidence.OriginalGitError} "
        + $"Exact lock path: {evidence.LockPath}. Manual remediation: {evidence.ManualRemediation}";

    private static HostStateGitRetryEvidence BuildEvidence(
        string outcome,
        int attempts,
        TimeSpan elapsed,
        string? lockPath,
        string originalError,
        string workingDirectory,
        HostStateGitRetryPolicy policy)
    {
        var exactLockPath = lockPath
            ?? Path.GetFullPath(Path.Combine(workingDirectory, ".git", "index.lock"));
        var remediation =
            $"inspect '{exactLockPath}' and confirm no Git process owns it before removing the stale lock manually; "
            + "intent-cli never removes, renames, moves, truncates, or repairs index.lock";
        return new HostStateGitRetryEvidence(
            outcome,
            attempts,
            Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds)),
            exactLockPath,
            originalError,
            remediation,
            policy);
    }

    private static TimeSpan DelayForRetry(
        HostStateGitRetryPolicy policy,
        int attemptsAlreadyMade,
        double jitterSample)
    {
        var exponent = Math.Min(attemptsAlreadyMade - 1, 30);
        var exponentialMilliseconds = policy.InitialDelayMilliseconds * Math.Pow(2, exponent);
        var baseMilliseconds = Math.Min(policy.MaxDelayMilliseconds, exponentialMilliseconds);
        var boundedJitter = Math.Clamp(jitterSample, 0, 1) * policy.JitterMilliseconds;
        return TimeSpan.FromMilliseconds(baseMilliseconds + boundedJitter);
    }

    private static string OriginalGitError(ClaimProcessResult result) =>
        (result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput).Trim();

    private static void Validate(HostStateGitRetryPolicy policy)
    {
        if (policy.MaxAttempts < 1
            || policy.WindowMilliseconds < 0
            || policy.InitialDelayMilliseconds < 0
            || policy.MaxDelayMilliseconds < 0
            || policy.JitterMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), "host-state Git retry policy values must be non-negative and attempts must be positive");
        }
    }
}

internal sealed record HostStateGitRetryEvidence(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("elapsed_milliseconds")] long ElapsedMilliseconds,
    [property: JsonPropertyName("lock_path")] string LockPath,
    [property: JsonPropertyName("original_git_error")] string OriginalGitError,
    [property: JsonPropertyName("manual_remediation")] string ManualRemediation,
    [property: JsonPropertyName("policy")] HostStateGitRetryPolicy Policy);

internal sealed class HostStateGitFailureException : InvalidOperationException
{
    internal HostStateGitFailureException(string operation, HostStateGitRetryEvidence evidence)
        : base($"Could not {operation}: {HostStateGitRetryRunner.TerminalDetail(evidence)}")
    {
        Evidence = evidence;
    }

    internal HostStateGitRetryEvidence Evidence { get; }
}
