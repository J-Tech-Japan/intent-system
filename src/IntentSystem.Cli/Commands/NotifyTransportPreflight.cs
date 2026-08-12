namespace IntentSystem.Cli.Commands;

/// <summary>
/// G675: prove that the transport process can be started before any pending
/// recipient is judged. The probe intentionally uses the same read-only
/// command that the liveness reader uses; a non-zero result means the process
/// did start and remains a transport response, while a start exception is a
/// cycle-level supervision dependency failure.
/// </summary>
internal static class NotifyTransportPreflight
{
    public static IReadOnlyList<NotifyTransportPreflightFailure> Check(
        string routingRoot,
        string domain,
        string team,
        IReadOnlyList<NotifyPendingDelegation> open,
        bool eventMode,
        INotifyProcessRunner runner,
        string herdrExecutable,
        string agmsgScriptsDirectory,
        string bashExecutable)
    {
        var modes = open
            .Select(record => string.IsNullOrWhiteSpace(record.TransportMode)
                ? SessionLayerMode.Agmsg
                : record.TransportMode!)
            .Where(SessionLayerMode.IsKnown)
            .ToHashSet(StringComparer.Ordinal);

        if (open.Count == 0 && !eventMode)
        {
            // Empty default teams have no recipient transport to judge. A
            // recorded herdr-only topology still has seat/absence readers,
            // so prove that dependency even before the first delegation.
            try
            {
                var recorded = SessionLayerModeStore.Resolve(routingRoot, domain, team);
                if (recorded.Source == SessionLayerModeSource.Recorded)
                {
                    modes.Add(recorded.Mode);
                }
            }
            catch (InvalidOperationException)
            {
                // The normal cycle readers retain their existing
                // mode-unreadable handling. This preflight only classifies
                // process-start failures and must not invent a second mode
                // verdict.
            }
        }

        if (eventMode)
        {
            modes.Add(SessionLayerMode.HerdrOnly);
        }

        var failures = new List<NotifyTransportPreflightFailure>();
        foreach (var mode in modes)
        {
            if (string.Equals(mode, SessionLayerMode.HerdrOnly, StringComparison.Ordinal))
            {
                TryStart(
                    runner,
                    herdrExecutable,
                    ["agent", "list"],
                    "herdr",
                    failures);
                continue;
            }

            var teamScript = Path.Combine(agmsgScriptsDirectory, "team.sh");
            if (!File.Exists(teamScript))
            {
                failures.Add(new NotifyTransportPreflightFailure(
                    "bash",
                    "transport-unavailable",
                    $"agmsg team roster script was not found at '{agmsgScriptsDirectory}'."));
                continue;
            }

            TryStart(
                runner,
                bashExecutable,
                [teamScript, team],
                "bash",
                failures);
        }

        return failures;
    }

    private static void TryStart(
        INotifyProcessRunner runner,
        string executable,
        IReadOnlyList<string> arguments,
        string binaryName,
        ICollection<NotifyTransportPreflightFailure> failures)
    {
        try
        {
            // Ignore stdout/stderr and exit status. This is a process-start
            // probe; response semantics remain owned by G648/G630 readers.
            _ = runner.Run(executable, arguments);
        }
        catch (InvalidOperationException exception)
        {
            failures.Add(new NotifyTransportPreflightFailure(
                binaryName,
                "transport-unavailable",
                exception.Message));
        }
    }
}

internal sealed record NotifyTransportPreflightFailure(
    string Binary,
    string Cause,
    string Error);
