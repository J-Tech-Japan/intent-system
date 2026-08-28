using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G679: serverless, Git-backed ownership for the two scope vocabularies that
/// are safe to coordinate across host clones. A successful plain push is the
/// only acquisition/release/takeover fact; local file creation is never
/// reported as ownership.
/// </summary>
internal static class ClaimCommand
{
    internal const string ClaimsDirectory = ".intent-cli/claims";
    internal const int DefaultMaxAttempts = 2;
    internal const int CleanupMaxAttempts = 3;
    internal static readonly TimeSpan CleanupAttemptTimeout = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(50);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static int ExecuteAcquire(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, ClaimOperation.Acquire);

    internal static int ExecuteAcquire(
        CliContext context,
        string[] args,
        TextWriter writer,
        TextWriter warningWriter,
        Action<string> deleteDirectory) =>
        Execute(context, args, writer, ClaimOperation.Acquire, warningWriter, deleteDirectory);

    public static int ExecuteRelease(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, ClaimOperation.Release);

    public static int ExecuteTakeover(CliContext context, string[] args, TextWriter writer) =>
        Execute(context, args, writer, ClaimOperation.Takeover);

    private static int Execute(
        CliContext context,
        string[] args,
        TextWriter writer,
        ClaimOperation operation)
        => Execute(context, args, writer, operation, Console.Error, null);

    private static int Execute(
        CliContext context,
        string[] args,
        TextWriter writer,
        ClaimOperation operation,
        TextWriter warningWriter,
        Action<string>? deleteDirectory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(warningWriter);

        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage(operation));
            return 0;
        }

        if (!TryParse(args, operation, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage(operation));
            return 1;
        }

        ClaimTransactionResult result;
        try
        {
            result = RunTransaction(context.RepoRoot, request!, warningWriter, deleteDirectory);
        }
        catch (HostStateGitFailureException exception)
        {
            result = new ClaimTransactionResult(
                "error", request!.Scope, ClaimPath(request.Scope), false, 0,
                null, null, null, exception.Message)
            {
                GitWriteRetry = exception.Evidence,
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            result = new ClaimTransactionResult(
                "error", request!.Scope, ClaimPath(request.Scope), false, 0,
                null, null, null, exception.Message);
        }

        WriteResult(writer, request!.Format, result);
        return result.Status is "acquired" or "released" or "taken-over" or "planned" ? 0 : 1;
    }

    internal static ClaimTransactionResult RunTransaction(string repoRoot, ClaimRequest request) =>
        RunTransaction(repoRoot, request, Console.Error);

    internal static ClaimTransactionResult RunTransaction(
        string repoRoot,
        ClaimRequest request,
        TextWriter warningWriter,
        Action<string>? deleteDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(warningWriter);
        deleteDirectory ??= path => Directory.Delete(path, recursive: true);

        var origin = RunGit(repoRoot, ["remote", "get-url", "origin"]);
        EnsureSuccess(origin, "resolve origin");
        var defaultBranchResult = RunGit(repoRoot, ["ls-remote", "--symref", "origin", "HEAD"]);
        EnsureSuccess(defaultBranchResult, "resolve origin default branch");
        var remote = origin.StandardOutput.Trim();
        if (remote.Length == 0)
        {
            throw new InvalidOperationException("claim requires an origin remote");
        }

        if (!TryParseRemoteDefaultBranch(defaultBranchResult.StandardOutput, out var defaultBranch))
        {
            throw new InvalidOperationException(
                "Could not resolve origin default branch from its HEAD symref; refusing to "
                + "fall back to the current branch.");
        }
        if (!request.Write)
        {
            return new ClaimTransactionResult(
                "planned", request.Scope, ClaimPath(request.Scope), false, 0,
                null, null, null,
                "Dry-run only. Re-run with --write; ownership exists only after a successful plain push.");
        }

        var targetRef = $"refs/heads/{defaultBranch}";

        HostStateGitRetryEvidence? lastGitWriteRetry = null;
        var hostFetch = RunGit(repoRoot, ["fetch", "--quiet", "origin", defaultBranch], hostStateWrite: true);
        CaptureRetry(ref lastGitWriteRetry, hostFetch);
        EnsureSuccess(hostFetch, "refresh origin default branch before claim transaction");

        ClaimRecord? lastObserved = null;
        for (var attempt = 1; attempt <= request.MaxAttempts; attempt++)
        {
            var transactionRoot = Path.Combine(
                Path.GetTempPath(), $"intent-cli-claim-{Guid.NewGuid():N}");
            var committed = false;
            var tolerateCleanupFailure = false;
            Exception? transactionFailure = null;
            try
            {
                var clone = RunGit(Path.GetTempPath(),
                    ["clone", "--quiet", "--single-branch", "--branch", defaultBranch, remote, transactionRoot]);
                EnsureSuccess(clone, "clone claim transaction workspace");

                // The sequence is deliberately visible and invariant: ff-only
                // pull, create/change, commit, then a non-forced push.
                var pull = RunGit(transactionRoot, ["pull", "--ff-only", "origin", defaultBranch], hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, pull);
                EnsureSuccess(pull, "fast-forward claim base");

                var relativeClaimPath = ClaimPath(request.Scope);
                var absoluteClaimPath = Path.Combine(
                    transactionRoot, relativeClaimPath.Replace('/', Path.DirectorySeparatorChar));
                var current = ReadClaim(absoluteClaimPath);
                lastObserved = current;
                if (request.Operation == ClaimOperation.Acquire
                    && current is not null
                    && string.Equals(current.Actor, request.Actor, StringComparison.Ordinal)
                    && string.Equals(current.Team, request.Team, StringComparison.Ordinal))
                {
                    // An identically held acquire is an intentional no-op.
                    // Its result must survive teardown, including an
                    // injected cleanup failure, so operators can distinguish
                    // "already held" from a broken pre-commit transaction.
                    tolerateCleanupFailure = true;
                    return Held(
                        request,
                        relativeClaimPath,
                        attempt,
                        current,
                        $"Scope is already held by actor '{current.Actor}' on team '{current.Team}'; "
                        + "no claim commit was needed (nothing to commit).") with
                    {
                        GitWriteRetry = lastGitWriteRetry,
                        TargetRef = targetRef,
                    };
                }

                if (request.Operation == ClaimOperation.Acquire && current is not null)
                {
                    return Held(request, relativeClaimPath, attempt, current) with
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
                if (request.Operation != ClaimOperation.Acquire && current is null)
                {
                    return new ClaimTransactionResult(
                        "not-held", request.Scope, relativeClaimPath, false, attempt,
                        null, null, null, "No active claim exists for this scope.")
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
                if (request.Operation == ClaimOperation.Release
                    && (!string.Equals(current!.Actor, request.Actor, StringComparison.Ordinal)
                        || !string.Equals(current.Team, request.Team, StringComparison.Ordinal)))
                {
                    return Held(request, relativeClaimPath, attempt, current,
                        "Only the complete attributed holder identity (actor and team) may release; use explicit takeover otherwise.") with
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
                if (request.Operation == ClaimOperation.Takeover
                    && !string.Equals(current!.Actor, request.DisplacedHolder, StringComparison.Ordinal))
                {
                    return Held(request, relativeClaimPath, attempt, current,
                        $"--displaced-holder must name the current holder '{current.Actor}'.") with
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }

                var now = DateTimeOffset.UtcNow;
                var head = RunGit(transactionRoot, ["rev-parse", "HEAD"]);
                EnsureSuccess(head, "resolve claim base commit");
                string? historyPath = null;

                if (request.Operation == ClaimOperation.Acquire)
                {
                    WriteClaim(absoluteClaimPath, new ClaimRecord(
                        "1", request.Scope, request.Actor, request.Team, now,
                        head.StandardOutput.Trim()));
                }
                else
                {
                    historyPath = WriteHistory(
                        transactionRoot, request, current!, now, head.StandardOutput.Trim());
                    if (request.Operation == ClaimOperation.Release)
                    {
                        File.Delete(absoluteClaimPath);
                    }
                    else
                    {
                        WriteClaim(absoluteClaimPath, new ClaimRecord(
                            "1", request.Scope, request.Actor, request.Team, now,
                            head.StandardOutput.Trim()));
                    }
                }

                var paths = historyPath is null
                    ? new[] { relativeClaimPath }
                    : new[] { relativeClaimPath, historyPath };
                var add = RunGit(transactionRoot, ["add", "--", .. paths], hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, add);
                EnsureSuccess(add, "stage claim transaction");
                var verb = request.Operation switch
                {
                    ClaimOperation.Acquire => "acquire",
                    ClaimOperation.Release => "release",
                    _ => "take over",
                };
                var commit = RunGit(transactionRoot,
                    ["-c", $"user.name={request.Actor}", "-c", $"user.email={SafeEmail(request)}",
                     "commit", "--quiet", "-m", $"claim: {verb} {request.Scope}"],
                    hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, commit);
                EnsureSuccess(commit, "commit claim transaction");
                var transactionCommit = RunGit(transactionRoot, ["rev-parse", "HEAD"]);
                EnsureSuccess(transactionCommit, "resolve claim transaction commit");
                var transactionCommitSha = transactionCommit.StandardOutput.Trim();

                var push = RunGit(
                    transactionRoot,
                    ["push", "origin", $"{transactionCommitSha}:{targetRef}"],
                    hostStateWrite: true);
                CaptureRetry(ref lastGitWriteRetry, push);
                if (push.ExitCode != 0 && push.RetryEvidence is not null)
                {
                    throw new HostStateGitFailureException("push claim transaction", push.RetryEvidence);
                }
                if (push.ExitCode == 0)
                {
                    // The plain push is the transaction boundary. Everything
                    // after it, including local refresh and temporary clone
                    // teardown, is best-effort and cannot change the result.
                    committed = true;
                    var status = request.Operation switch
                    {
                        ClaimOperation.Acquire => "acquired",
                        ClaimOperation.Release => "released",
                        _ => "taken-over",
                    };
                    var detail = RefreshInvokingClone(
                        repoRoot,
                        defaultBranch,
                        ref lastGitWriteRetry,
                        "The plain push succeeded; this is the ownership transition fact.");
                    return new ClaimTransactionResult(
                        status, request.Scope, relativeClaimPath, true, attempt,
                        request.Operation == ClaimOperation.Release ? null : request.Actor,
                        request.Operation == ClaimOperation.Takeover ? current!.Actor : null,
                        transactionCommitSha,
                        detail,
                        historyPath)
                    {
                        GitWriteRetry = lastGitWriteRetry,
                        TargetRef = targetRef,
                    };
                }

                var fetch = RunGit(transactionRoot, ["fetch", "origin", defaultBranch]);
                EnsureSuccess(fetch, "inspect rejected claim push");
                var remoteDefaultHead = RunGit(transactionRoot, ["rev-parse", $"origin/{defaultBranch}"]);
                var remoteCommitMatches = remoteDefaultHead.ExitCode == 0
                    && string.Equals(remoteDefaultHead.StandardOutput.Trim(), transactionCommitSha, StringComparison.Ordinal);

                var remoteClaim = RunGit(transactionRoot,
                    ["show", $"origin/{defaultBranch}:{relativeClaimPath}"]);
                if (remoteClaim.ExitCode == 0)
                {
                    var holder = JsonSerializer.Deserialize<ClaimRecord>(remoteClaim.StandardOutput, JsonOptions)
                        ?? throw new InvalidOperationException("remote claim record was empty");
                    if (remoteCommitMatches
                        && request.Operation != ClaimOperation.Release
                        && string.Equals(holder.Actor, request.Actor, StringComparison.Ordinal)
                        && string.Equals(holder.Team, request.Team, StringComparison.Ordinal)
                        && string.Equals(holder.BaseCommit, head.StandardOutput.Trim(), StringComparison.Ordinal))
                    {
                        // A receive-side failure can be reported after the
                        // remote ref has advanced. The fetched commit and
                        // resulting claim state are the durable fact, so
                        // cleanup must see the committed boundary.
                        committed = true;
                        var status = request.Operation switch
                        {
                            ClaimOperation.Acquire => "acquired",
                            ClaimOperation.Release => "released",
                            _ => "taken-over",
                        };
                        var detail = RefreshInvokingClone(
                            repoRoot,
                            defaultBranch,
                            ref lastGitWriteRetry,
                            "The remote branch contains the transaction commit after the push process returned a failure; verified remote state is the ownership transition fact.");
                        return new ClaimTransactionResult(
                            status, request.Scope, relativeClaimPath, true, attempt,
                            request.Operation == ClaimOperation.Release ? null : request.Actor,
                            request.Operation == ClaimOperation.Takeover ? current!.Actor : null,
                            transactionCommitSha,
                            detail,
                            historyPath)
                        {
                            GitWriteRetry = lastGitWriteRetry,
                            TargetRef = targetRef,
                        };
                    }

                    var samePreexistingClaim = current is not null
                        && holder == current;
                    if (request.Operation == ClaimOperation.Acquire || !samePreexistingClaim)
                    {
                        return Held(request, relativeClaimPath, attempt, holder,
                            "The push was rejected and the same scope now exists on origin.") with
                        {
                            GitWriteRetry = lastGitWriteRetry,
                        };
                    }
                }

                if (remoteCommitMatches
                    && request.Operation == ClaimOperation.Release
                    && remoteClaim.ExitCode != 0)
                {
                    committed = true;
                    var status = "released";
                    var detail = RefreshInvokingClone(
                        repoRoot,
                        defaultBranch,
                        ref lastGitWriteRetry,
                        "The remote branch contains the transaction commit after the push process returned a failure; verified remote state is the ownership transition fact.");
                    return new ClaimTransactionResult(
                        status, request.Scope, relativeClaimPath, true, attempt,
                        null, null, transactionCommitSha, detail, historyPath)
                    {
                        GitWriteRetry = lastGitWriteRetry,
                        TargetRef = targetRef,
                    };
                }

                // Unrelated remote advance: discard this isolated workspace and
                // reapply from a fresh ff-only base. No force, rebase, or merge.
                if (attempt == request.MaxAttempts)
                {
                    return new ClaimTransactionResult(
                        "retry-exhausted", request.Scope, relativeClaimPath, false, attempt,
                        lastObserved?.Actor, null, null,
                        $"Push was rejected by unrelated remote advance after {attempt} bounded attempt(s).")
                    {
                        GitWriteRetry = lastGitWriteRetry,
                    };
                }
            }
            catch (Exception exception)
            {
                transactionFailure = exception;
                throw;
            }
            finally
            {
                try
                {
                    CleanupTransactionRoot(
                        transactionRoot,
                        committed,
                        tolerateCleanupFailure,
                        warningWriter,
                        deleteDirectory);
                }
                catch (Exception cleanupFailure) when (transactionFailure is not null)
                {
                    try
                    {
                        warningWriter.WriteLine(
                            "warning: claim transaction failed before commit; the original transaction "
                            + "failure is preserved. Cleanup also failed: " + cleanupFailure.Message);
                    }
                    catch
                    {
                        // A broken warning sink must not replace the original
                        // pre-commit transaction failure.
                    }
                }
            }
        }

        throw new InvalidOperationException("claim transaction exhausted unexpectedly");
    }

    private static void CleanupTransactionRoot(
        string transactionRoot,
        bool committed,
        bool tolerateCleanupFailure,
        TextWriter warningWriter,
        Action<string> deleteDirectory)
    {
        if (!Directory.Exists(transactionRoot)) return;

        Exception? lastFailure = null;
        var attempts = 0;
        var timedOut = false;
        try
        {
            // At most 3 * 250 ms of bounded delete attempts plus 2 * 50 ms
            // backoff delays are added. A timed-out delete is not retried
            // while its bounded worker may still be touching the directory.
            for (var attempt = 1; attempt <= CleanupMaxAttempts; attempt++)
            {
                attempts = attempt;
                if (attempt > 1) Thread.Sleep(CleanupRetryDelay);

                if (TryDeleteTransactionRoot(
                        transactionRoot, deleteDirectory, out lastFailure, out timedOut))
                {
                    return;
                }

                if (timedOut) break;
            }
        }
        catch (Exception exception)
        {
            lastFailure = exception;
        }

        if (tolerateCleanupFailure)
        {
            WriteNoOpCleanupWarning(warningWriter, transactionRoot, attempts, lastFailure);
            return;
        }
        if (committed)
        {
            var detail = lastFailure?.Message;
            try
            {
                warningWriter.WriteLine(
                    $"warning: claim transaction committed successfully, but best-effort cleanup "
                    + $"could not remove temporary directory '{transactionRoot}' after {attempts} "
                    + $"bounded attempt(s); the claim result and exit code are unchanged. "
                    + $"The leftover path remains under the OS temp root."
                    + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Last error: {detail}"));
            }
            catch
            {
                // A broken warning sink must not cross the commit boundary
                // and turn a successful claim into a failed command.
            }
            return;
        }

        throw new IOException(
            $"Could not clean up claim transaction temporary directory '{transactionRoot}' before "
            + "the claim state was committed.", lastFailure);
    }

    private static void WriteNoOpCleanupWarning(
        TextWriter warningWriter,
        string transactionRoot,
        int attempts,
        Exception? lastFailure)
    {
        var detail = lastFailure?.Message;
        try
        {
            warningWriter.WriteLine(
                $"warning: claim acquire found the scope already held; no claim commit was needed "
                + $"(nothing to commit). Best-effort cleanup could not remove temporary directory "
                + $"'{transactionRoot}' after {attempts} bounded attempt(s); the already-held claim "
                + $"result and exit code are unchanged. The leftover path remains under the OS temp root."
                + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Last error: {detail}"));
        }
        catch
        {
            // A broken warning sink must not replace the already-held result.
        }
    }

    private static string RefreshInvokingClone(
        string repoRoot,
        string defaultBranch,
        ref HostStateGitRetryEvidence? lastGitWriteRetry,
        string committedDetail)
    {
        try
        {
            var localRefresh = RunGit(
                repoRoot,
                ["fetch", "--quiet", "origin", defaultBranch],
                hostStateWrite: true);
            CaptureRetry(ref lastGitWriteRetry, localRefresh);
            return localRefresh.ExitCode == 0
                ? committedDetail
                : committedDetail
                    + " The invoking clone could not refresh the origin default-branch tracking ref: "
                    + localRefresh.StandardError.Trim();
        }
        catch (Exception exception)
        {
            return committedDetail
                + " The invoking clone refresh could not run: "
                + exception.Message;
        }
    }

    private static bool TryDeleteTransactionRoot(
        string transactionRoot,
        Action<string> deleteDirectory,
        out Exception? failure,
        out bool timedOut)
    {
        failure = null;
        timedOut = false;

        Task deletion;
        try
        {
            deletion = Task.Run(() => deleteDirectory(transactionRoot));
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }

        try
        {
            if (!deletion.Wait(CleanupAttemptTimeout))
            {
                ObserveFault(deletion);
                timedOut = true;
                failure = new TimeoutException(
                    $"temporary directory deletion exceeded {CleanupAttemptTimeout.TotalMilliseconds:0} ms");
                return false;
            }

            deletion.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }

        if (Directory.Exists(transactionRoot))
        {
            failure = new IOException("temporary directory deletion returned without removing the directory");
            return false;
        }

        return true;
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal static bool TryParseRemoteDefaultBranch(
        string output,
        out string defaultBranch)
    {
        defaultBranch = string.Empty;
        string? branch = null;
        string? headObject = null;

        foreach (var rawLine in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("ref: refs/heads/", StringComparison.Ordinal)
                && line.EndsWith("\tHEAD", StringComparison.Ordinal))
            {
                const string prefix = "ref: refs/heads/";
                const string suffix = "\tHEAD";
                var candidate = line[prefix.Length..(line.Length - suffix.Length)];
                if (!IsSafeRemoteBranch(candidate)
                    || (branch is not null
                        && !string.Equals(branch, candidate, StringComparison.Ordinal)))
                {
                    return false;
                }
                branch = candidate;
                continue;
            }

            var separator = line.IndexOf('\t');
            if (separator > 0
                && string.Equals(line[(separator + 1)..], "HEAD", StringComparison.Ordinal))
            {
                var candidate = line[..separator];
                if (!IsHexObjectId(candidate)
                    || (headObject is not null
                        && !string.Equals(headObject, candidate, StringComparison.Ordinal)))
                {
                    return false;
                }
                headObject = candidate;
            }
        }

        if (branch is null || headObject is null)
        {
            return false;
        }

        defaultBranch = branch;
        return true;
    }

    private static bool IsSafeRemoteBranch(string value)
    {
        if (value.Length == 0
            || value.StartsWith("-", StringComparison.Ordinal)
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.EndsWith("/", StringComparison.Ordinal)
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("//", StringComparison.Ordinal)
            || value.Contains("@{", StringComparison.Ordinal)
            || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)
                || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
        {
            return false;
        }

        return value.Split('/').All(segment => segment is not ("" or "." or ".."));
    }

    private static bool IsHexObjectId(string value) =>
        value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    internal static string ClaimPath(string scope)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))).ToLowerInvariant();
        return $"{ClaimsDirectory}/{digest}.json";
    }

    private static ClaimTransactionResult Held(
        ClaimRequest request,
        string path,
        int attempt,
        ClaimRecord holder,
        string? detail = null) =>
        new ClaimTransactionResult(
            "held", request.Scope, path, false, attempt, holder.Actor, null, null,
            detail ?? $"Scope is held by actor '{holder.Actor}' on team '{holder.Team}'.")
        {
            HolderTeam = holder.Team,
        };

    private static string WriteHistory(
        string transactionRoot,
        ClaimRequest request,
        ClaimRecord current,
        DateTimeOffset now,
        string baseCommit)
    {
        var claimName = Path.GetFileNameWithoutExtension(ClaimPath(request.Scope));
        var operation = request.Operation == ClaimOperation.Release ? "release" : "takeover";
        var relative = $"{ClaimsDirectory}/history/{claimName}/{now:yyyyMMddTHHmmssfffffffZ}-{operation}.json";
        var absolute = Path.Combine(transactionRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var history = new ClaimHistoryRecord(
            "1", operation, request.Scope, request.Actor, request.Team, now,
            request.Reason!, current.Actor, current.Team, current.ClaimedAt, baseCommit);
        File.WriteAllText(absolute, JsonSerializer.Serialize(history, JsonOptions) + Environment.NewLine);
        return relative;
    }

    private static ClaimRecord? ReadClaim(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ClaimRecord>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"claim record '{path}' was empty");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"claim record '{path}' is invalid: {exception.Message}");
        }
    }

    private static void WriteClaim(string path, ClaimRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
    }

    private static string SafeEmail(ClaimRequest request)
    {
        var local = new string(request.Actor.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return $"{(local.Length == 0 ? "intent-cli" : local)}@claims.invalid";
    }

    private static ClaimProcessResult RunGit(
        string workdir,
        IReadOnlyList<string> arguments,
        bool hostStateWrite = false)
    {
        if (hostStateWrite)
        {
            return HostStateGitRetryRunner.Run(
                workdir,
                arguments,
                () => RunGitProcess(workdir, arguments));
        }

        return RunGitProcess(workdir, arguments);
    }

    private static ClaimProcessResult RunGitProcess(string workdir, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start git");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ClaimProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void EnsureSuccess(ClaimProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            if (result.RetryEvidence is not null)
            {
                throw new HostStateGitFailureException(operation, result.RetryEvidence);
            }

            throw new InvalidOperationException(
                $"Could not {operation}: {result.StandardError.Trim()}");
        }
    }

    private static void CaptureRetry(
        ref HostStateGitRetryEvidence? target,
        ClaimProcessResult result)
    {
        if (result.RetryEvidence is not null)
        {
            target = result.RetryEvidence;
        }
    }

    private static bool TryParse(
        string[] args,
        ClaimOperation operation,
        out ClaimRequest? request,
        out string error)
    {
        string? scope = null;
        string? actor = null;
        string? team = null;
        string? reason = null;
        string? displacedHolder = null;
        var format = "json";
        var write = false;
        var maxAttempts = DefaultMaxAttempts;

        for (var i = 0; i < args.Length; i++)
        {
            string NeedValue(string option)
            {
                if (++i >= args.Length) throw new ArgumentException($"{option} requires a value");
                return args[i];
            }

            try
            {
                switch (args[i])
                {
                    case "--scope": scope = NeedValue("--scope"); break;
                    case "--actor": actor = NeedValue("--actor"); break;
                    case "--team": team = NeedValue("--team"); break;
                    case "--reason": reason = NeedValue("--reason"); break;
                    case "--displaced-holder": displacedHolder = NeedValue("--displaced-holder"); break;
                    case "--format": format = NeedValue("--format"); break;
                    case "--max-attempts":
                        if (!int.TryParse(NeedValue("--max-attempts"), out maxAttempts)
                            || maxAttempts is < 1 or > 5)
                            throw new ArgumentException("--max-attempts must be between 1 and 5");
                        break;
                    case "--write": write = true; break;
                    default: throw new ArgumentException($"Unknown option '{args[i]}'");
                }
            }
            catch (ArgumentException exception)
            {
                request = null;
                error = exception.Message;
                return false;
            }
        }

        if (!TryValidateScope(scope, out error))
        {
            request = null;
            return false;
        }
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(team))
        {
            request = null;
            error = "--actor and --team are required attribution";
            return false;
        }
        if (format is not ("json" or "markdown"))
        {
            request = null;
            error = "--format must be json or markdown";
            return false;
        }
        if (operation != ClaimOperation.Acquire && string.IsNullOrWhiteSpace(reason))
        {
            request = null;
            error = "release and takeover require --reason";
            return false;
        }
        if (operation == ClaimOperation.Takeover && string.IsNullOrWhiteSpace(displacedHolder))
        {
            request = null;
            error = "takeover requires --displaced-holder";
            return false;
        }

        request = new ClaimRequest(operation, scope!, actor.Trim(), team.Trim(), reason?.Trim(),
            displacedHolder?.Trim(), write, format, maxAttempts);
        error = string.Empty;
        return true;
    }

    internal static bool TryValidateScope(string? scope, out string error)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            error = "--scope is required";
            return false;
        }

        var parts = scope.Split(':');
        var validToken = static (string value) => value.Length > 0
            && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '+');
        if (parts.Length == 2 && parts[0] == "execution-unit" && validToken(parts[1]))
        {
            error = string.Empty;
            return true;
        }
        if (parts.Length == 3 && parts[0] == "release-prep")
        {
            var repoParts = parts[1].Split('/');
            if (repoParts.Length == 2 && repoParts.All(validToken) && validToken(parts[2]))
            {
                error = string.Empty;
                return true;
            }
        }

        error = "scope must be execution-unit:<EU> or release-prep:<owner/repo>:<version>";
        return false;
    }

    private static string Usage(ClaimOperation operation) => operation switch
    {
        ClaimOperation.Acquire =>
            "Usage: intent-cli claim acquire --scope <execution-unit:EU|release-prep:owner/repo:version> --actor <actor> --team <team> [--max-attempts 2] [--write] [--format json|markdown]",
        ClaimOperation.Release =>
            "Usage: intent-cli claim release --scope <scope> --actor <holder> --team <team> --reason <reason> [--write] [--format json|markdown]",
        _ =>
            "Usage: intent-cli claim takeover --scope <scope> --actor <actor> --team <team> --displaced-holder <actor> --reason <reason> [--max-attempts 2] [--write] [--format json|markdown]",
    };

    private static void WriteResult(TextWriter writer, string format, ClaimTransactionResult result)
    {
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# Claim {result.Status}");
        writer.WriteLine("- preview_status: preview-through-1.x");
        writer.WriteLine($"- scope: `{result.Scope}`");
        writer.WriteLine($"- claim_path: `{result.ClaimPath}`");
        writer.WriteLine($"- push_succeeded: {(result.PushSucceeded ? "true" : "false")}");
        if (result.TargetRef is not null) writer.WriteLine($"- target_ref: `{result.TargetRef}`");
        if (result.Holder is not null) writer.WriteLine($"- holder: {result.Holder}");
        if (result.HolderTeam is not null) writer.WriteLine($"- holder_team: {result.HolderTeam}");
        if (result.DisplacedHolder is not null) writer.WriteLine($"- displaced_holder: {result.DisplacedHolder}");
        if (result.Commit is not null) writer.WriteLine($"- commit: `{result.Commit}`");
        if (result.HistoryPath is not null) writer.WriteLine($"- history_path: `{result.HistoryPath}`");
        if (result.GitWriteRetry is not null)
        {
            writer.WriteLine($"- git_write_retry: outcome={result.GitWriteRetry.Outcome}; attempts={result.GitWriteRetry.Attempts}; elapsed_milliseconds={result.GitWriteRetry.ElapsedMilliseconds}; lock_path=`{result.GitWriteRetry.LockPath}`");
            writer.WriteLine($"- manual_remediation: {result.GitWriteRetry.ManualRemediation}");
        }
        writer.WriteLine($"- detail: {result.Detail}");
    }
}

internal enum ClaimOperation { Acquire, Release, Takeover }

internal sealed record ClaimRequest(
    ClaimOperation Operation,
    string Scope,
    string Actor,
    string Team,
    string? Reason,
    string? DisplacedHolder,
    bool Write,
    string Format,
    int MaxAttempts);

internal sealed record ClaimRecord(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("claimed_at")] DateTimeOffset ClaimedAt,
    [property: JsonPropertyName("base_commit")] string BaseCommit);

internal sealed record ClaimHistoryRecord(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("team")] string Team,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("displaced_holder")] string DisplacedHolder,
    [property: JsonPropertyName("displaced_team")] string DisplacedTeam,
    [property: JsonPropertyName("displaced_claimed_at")] DateTimeOffset DisplacedClaimedAt,
    [property: JsonPropertyName("base_commit")] string BaseCommit);

internal sealed record ClaimTransactionResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("claim_path")] string ClaimPath,
    [property: JsonPropertyName("push_succeeded")] bool PushSucceeded,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("holder")] string? Holder,
    [property: JsonPropertyName("displaced_holder")] string? DisplacedHolder,
    [property: JsonPropertyName("commit")] string? Commit,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("history_path")] string? HistoryPath = null)
{
    [JsonPropertyName("preview_status")]
    public string PreviewStatus => "preview-through-1.x";

    [JsonPropertyName("holder_team")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HolderTeam { get; init; }

    [JsonPropertyName("target_ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetRef { get; init; }

    [JsonPropertyName("git_write_retry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HostStateGitRetryEvidence? GitWriteRetry { get; init; }
}

internal sealed record ClaimProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    HostStateGitRetryEvidence? RetryEvidence = null);
