using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G727: observes whether a host-state answer was computed from the current
/// checkout without changing that checkout. The remote is queried with
/// <c>git ls-remote --symref origin HEAD</c>; unlike fetch/pull/reset, that
/// command does not update local refs or the working tree. If the remote
/// cannot be queried, the result is explicitly unknown rather than current.
/// </summary>
internal static class CheckoutFreshnessProbe
{
    public const string Current = "current";
    public const string Stale = "stale";
    public const string Unknown = "unknown";

    // Keep the timeout short enough that a stalled host wake remains useful,
    // while allowing ordinary local and remote Git calls to complete.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public static CheckoutFreshnessObservation? Capture(
        string repoRoot,
        IGitRemoteCommandRunner runner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(runner);

        // A unit-test workspace or a host-side directory can be a valid
        // intent-cli root without being a Git checkout. There is no checkout
        // freshness claim to make in that shape, so preserve the historical
        // output instead of manufacturing an unknown banner for every test
        // and non-Git invocation.
        var gitMarker = Path.Combine(repoRoot, ".git");
        if (!File.Exists(gitMarker) && !Directory.Exists(gitMarker))
        {
            return null;
        }

        string localHead;
        try
        {
            var local = runner.Run(repoRoot, ["rev-parse", "HEAD"]);
            if (local.ExitCode != 0 || string.IsNullOrWhiteSpace(local.StdOut))
            {
                return UnknownObservation(
                    null,
                    null,
                    null,
                    "the local checkout HEAD could not be read");
            }

            localHead = local.StdOut.Trim();
        }
        catch (Exception exception) when (IsProbeFailure(exception))
        {
            return UnknownObservation(
                null,
                null,
                null,
                $"the local checkout HEAD could not be read ({exception.Message})");
        }

        GitRemoteCommandResult remote;
        try
        {
            remote = runner.Run(repoRoot, ["ls-remote", "--symref", "origin", "HEAD"]);
        }
        catch (Exception exception) when (IsProbeFailure(exception))
        {
            return UnknownObservation(
                localHead,
                null,
                null,
                $"the origin default branch could not be queried ({exception.Message})");
        }

        if (remote.ExitCode != 0)
        {
            if (remote.TimedOut)
            {
                return UnknownObservation(
                    localHead,
                    null,
                    null,
                    FirstNonEmpty(
                        remote.StdErr,
                        $"git ls-remote --symref origin HEAD timed out after {DefaultTimeout.TotalSeconds:0.###} seconds"));
            }

            var detail = FirstNonEmpty(remote.StdErr, "the origin default branch could not be queried");
            return UnknownObservation(localHead, null, null, detail);
        }

        if (!TryParseRemoteHead(remote.StdOut, out var defaultBranch, out var remoteHead))
        {
            return UnknownObservation(
                localHead,
                null,
                null,
                "the origin response did not identify a default branch and HEAD");
        }

        return DetermineContainment(repoRoot, runner, localHead, defaultBranch, remoteHead);
    }

    private static CheckoutFreshnessObservation DetermineContainment(
        string repoRoot,
        IGitRemoteCommandRunner runner,
        string localHead,
        string defaultBranch,
        string remoteHead)
    {
        GitRemoteCommandResult remoteTipObject;
        try
        {
            // A remote tip that is not already in the local object store cannot
            // be reachable from HEAD. Treat that absence as a real stale
            // result without fetching or otherwise mutating the checkout.
            remoteTipObject = runner.Run(
                repoRoot,
                ["cat-file", "-e", $"{remoteHead}^{{commit}}"]);
        }
        catch (Exception exception) when (IsProbeFailure(exception))
        {
            return UnknownObservation(
                localHead,
                defaultBranch,
                remoteHead,
                $"the origin default branch tip could not be checked locally ({exception.Message})");
        }

        if (remoteTipObject.TimedOut)
        {
            return UnknownObservation(
                localHead,
                defaultBranch,
                remoteHead,
                FirstNonEmpty(
                    remoteTipObject.StdErr,
                    $"git cat-file -e {remoteHead}^{{commit}} timed out after {DefaultTimeout.TotalSeconds:0.###} seconds"));
        }

        if (remoteTipObject.ExitCode != 0)
        {
            return new CheckoutFreshnessObservation
            {
                Status = Stale,
                LocalHead = localHead,
                DefaultBranch = defaultBranch,
                RemoteHead = remoteHead,
            };
        }

        GitRemoteCommandResult ancestry;
        try
        {
            ancestry = runner.Run(
                repoRoot,
                ["merge-base", "--is-ancestor", remoteHead, "HEAD"]);
        }
        catch (Exception exception) when (IsProbeFailure(exception))
        {
            return UnknownObservation(
                localHead,
                defaultBranch,
                remoteHead,
                $"the origin default branch tip could not be compared locally ({exception.Message})");
        }

        if (ancestry.TimedOut)
        {
            return UnknownObservation(
                localHead,
                defaultBranch,
                remoteHead,
                FirstNonEmpty(
                    ancestry.StdErr,
                    $"git merge-base --is-ancestor {remoteHead} HEAD timed out after {DefaultTimeout.TotalSeconds:0.###} seconds"));
        }

        if (ancestry.ExitCode == 0)
        {
            return new CheckoutFreshnessObservation
            {
                Status = Current,
                LocalHead = localHead,
                DefaultBranch = defaultBranch,
                RemoteHead = remoteHead,
            };
        }

        if (ancestry.ExitCode != 1)
        {
            return UnknownObservation(
                localHead,
                defaultBranch,
                remoteHead,
                FirstNonEmpty(
                    ancestry.StdErr,
                    "the origin default branch tip could not be compared locally"));
        }

        return new CheckoutFreshnessObservation
        {
            Status = Stale,
            LocalHead = localHead,
            DefaultBranch = defaultBranch,
            RemoteHead = remoteHead,
        };
    }
    private static CheckoutFreshnessObservation UnknownObservation(
        string? localHead,
        string? defaultBranch,
        string? remoteHead,
        string reason) => new()
        {
            Status = Unknown,
            LocalHead = localHead,
            DefaultBranch = defaultBranch,
            RemoteHead = remoteHead,
            Reason = reason,
        };

    private static bool TryParseRemoteHead(
        string output,
        out string defaultBranch,
        out string remoteHead)
    {
        defaultBranch = string.Empty;
        remoteHead = string.Empty;

        foreach (var rawLine in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("ref: refs/heads/", StringComparison.Ordinal)
                && line.EndsWith("\tHEAD", StringComparison.Ordinal))
            {
                const string refPrefix = "ref: refs/heads/";
                const string headSuffix = "\tHEAD";
                defaultBranch = line[refPrefix.Length..(line.Length - headSuffix.Length)];
                continue;
            }

            var separator = line.IndexOf('\t');
            if (separator > 0
                && string.Equals(line[(separator + 1)..], "HEAD", StringComparison.Ordinal))
            {
                remoteHead = line[..separator];
            }
        }

        return !string.IsNullOrWhiteSpace(defaultBranch)
            && !string.IsNullOrWhiteSpace(remoteHead);
    }

    private static string FirstNonEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsProbeFailure(Exception exception) =>
        exception is IOException
            or InvalidOperationException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception;
}

internal sealed record CheckoutFreshnessObservation
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("local_head")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalHead { get; init; }

    [JsonPropertyName("default_branch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultBranch { get; init; }

    [JsonPropertyName("remote_head")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RemoteHead { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonIgnore]
    public string Warning => Status switch
    {
        CheckoutFreshnessProbe.Stale =>
            $"checkout freshness: stale; this answer was computed from local HEAD {LocalHead} "
            + $"but origin/{DefaultBranch} is {RemoteHead}. Sync the checkout and re-run the report; "
            + "this command is read-only and does not fetch, pull, reset, or sync.",
        CheckoutFreshnessProbe.Unknown =>
            $"checkout freshness: unknown; {Reason ?? "the checkout freshness probe could not determine a result"}. "
            + "Do not treat this report as evidence that the checkout is current; verify the checkout and re-run. "
            + "This command is read-only and does not fetch, pull, reset, or sync.",
        _ => string.Empty,
    };
}
