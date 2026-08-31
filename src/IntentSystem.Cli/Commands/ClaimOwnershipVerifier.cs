using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G680: the one read-only ownership judgment consumed by every surface that
/// starts execution-unit or release-preparation work. The G679 transaction
/// primitive remains the sole writer; this class only reads its active record.
/// </summary>
internal static class ClaimOwnershipVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal static ClaimOwnershipVerification Verify(
        string repoRoot,
        string scope,
        string? invokingTeam,
        bool allowUnheld = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        // Vocabulary is part of the G679 primitive contract. Validate before
        // either local or canonical no-store compatibility can pass.
        if (!ClaimCommand.TryValidateScope(scope, out var scopeError))
        {
            return Refused(
                ClaimOwnershipVerification.StatusInvalid,
                scope,
                invokingTeam,
                null,
                null,
                $"claim verification refused scope '{scope}': {scopeError}",
                storeConfigured: false);
        }

        var evidence = ReadCanonicalEvidence(repoRoot, scope);
        if (!evidence.Available)
        {
            return Refused(
                ClaimOwnershipVerification.StatusCanonicalUnavailable,
                scope,
                invokingTeam,
                null,
                null,
                $"claim verification refused scope '{scope}': fresh canonical Git evidence is unavailable ({evidence.Detail}).");
        }

        if (!evidence.StoreConfigured)
        {
            return new ClaimOwnershipVerification(
                Passed: true,
                Status: ClaimOwnershipVerification.StatusNotConfigured,
                Scope: scope,
                StoreConfigured: false,
                InvokingTeam: invokingTeam,
                Holder: null,
                HolderTeam: null,
                Detail: "No claims store is configured; legacy single-team behavior applies unchanged.");
        }

        if (evidence.MetadataBranchOnly)
        {
            return Refused(
                ClaimOwnershipVerification.StatusMetadataBranchOnly,
                scope,
                invokingTeam,
                null,
                null,
                evidence.Detail);
        }

        if (evidence.RecordJson is null)
        {
            if (allowUnheld)
            {
                return new ClaimOwnershipVerification(
                    Passed: true,
                    Status: ClaimOwnershipVerification.StatusUnheldAvailable,
                    Scope: scope,
                    StoreConfigured: true,
                    InvokingTeam: invokingTeam,
                    Holder: null,
                    HolderTeam: null,
                    Detail: $"Scope '{scope}' is unheld and remains eligible to be claimed before work starts.");
            }

            return Refused(
                ClaimOwnershipVerification.StatusUnheld,
                scope,
                invokingTeam,
                null,
                null,
                $"claim verification refused scope '{scope}': holder is none (unheld); acquire the scope before starting work.");
        }

        ClaimRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<ClaimRecord>(evidence.RecordJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            return Refused(
                ClaimOwnershipVerification.StatusInvalid,
                scope,
                invokingTeam,
                null,
                null,
                $"claim verification refused scope '{scope}': active record is invalid ({exception.Message}).");
        }
        if (record is null || !string.Equals(record.Scope, scope, StringComparison.Ordinal))
        {
            return Refused(
                ClaimOwnershipVerification.StatusInvalid,
                scope,
                invokingTeam,
                record?.Actor,
                record?.Team,
                $"claim verification refused scope '{scope}': active record is empty or names a different scope.");
        }

        if (string.IsNullOrWhiteSpace(invokingTeam))
        {
            return Refused(
                ClaimOwnershipVerification.StatusTeamRequired,
                scope,
                invokingTeam,
                record.Actor,
                record.Team,
                $"claim verification refused scope '{scope}': holder actor '{record.Actor}' on team '{record.Team}'; --team is required on a claims-enabled host.");
        }

        if (!string.Equals(record.Team, invokingTeam, StringComparison.Ordinal))
        {
            return Refused(
                ClaimOwnershipVerification.StatusHeldByOtherTeam,
                scope,
                invokingTeam,
                record.Actor,
                record.Team,
                $"claim verification refused scope '{scope}': holder actor '{record.Actor}' on team '{record.Team}'; invoking team '{invokingTeam}' does not hold it.");
        }

        return new ClaimOwnershipVerification(
            Passed: true,
            Status: ClaimOwnershipVerification.StatusOwned,
            Scope: scope,
            StoreConfigured: true,
            InvokingTeam: invokingTeam,
            Holder: record.Actor,
            HolderTeam: record.Team,
            Detail: $"Scope '{scope}' is held by actor '{record.Actor}' on invoking team '{record.Team}'.");
    }

    internal static ClaimStoreProbe ProbeStore(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        var evidence = ReadCanonicalEvidence(repoRoot, "execution-unit:claim-store-probe");
        return new ClaimStoreProbe(evidence.Available, evidence.StoreConfigured, evidence.Detail);
    }

    /// <summary>
    /// G717: construct a fail-closed observation when a consumer cannot
    /// resolve the execution-unit claim evidence needed to interpret a stale
    /// lifecycle label. This is read-only; claim acquisition/release remains
    /// exclusively owned by the G679 transaction primitive.
    /// </summary>
    internal static ClaimOwnershipVerification Unavailable(string scope, string detail) =>
        Refused(
            ClaimOwnershipVerification.StatusCanonicalUnavailable,
            scope,
            invokingTeam: null,
            holder: null,
            holderTeam: null,
            detail: detail);

    /// <summary>
    /// A Git worktree must use the pushed remote fact, never local absence or
    /// a stale local record. Non-Git roots retain the local evidence path used
    /// by deterministic command fixtures and embedded callers.
    /// </summary>
    private static CanonicalClaimEvidence ReadCanonicalEvidence(string repoRoot, string scope)
    {
        // G763: ordinary verification is deliberately canonical-only. The
        // explicit `claim stranded` surface is the only claim command that
        // reads a configured metadata branch for migration diagnostics.
        var inside = RunGit(repoRoot, ["rev-parse", "--is-inside-work-tree"]);
        if (inside.ExitCode != 0
            || !string.Equals(inside.StandardOutput.Trim(), "true", StringComparison.Ordinal))
        {
            return ReadLocalEvidence(repoRoot, scope);
        }

        ClaimRemoteDefaultBranch canonicalBranch;
        try
        {
            canonicalBranch = ClaimCommand.ResolveRemoteDefaultBranch(repoRoot);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return CanonicalClaimEvidence.Unavailable(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "resolving origin default branch failed"
                    : exception.Message);
        }

        var remoteRef = $"refs/remotes/origin/{canonicalBranch.Name}";
        var fetch = RunGit(repoRoot,
            ["fetch", "--quiet", "origin", $"+refs/heads/{canonicalBranch.Name}:{remoteRef}"]);
        if (fetch.ExitCode != 0)
        {
            return CanonicalClaimEvidence.Unavailable(
                string.IsNullOrWhiteSpace(fetch.StandardError)
                    ? "fetching the canonical default branch from origin failed"
                    : fetch.StandardError.Trim());
        }

        var tree = RunGit(repoRoot,
            ["ls-tree", "-r", "--name-only", remoteRef, "--", ClaimCommand.ClaimsDirectory]);
        if (tree.ExitCode != 0)
        {
            return CanonicalClaimEvidence.Unavailable(
                string.IsNullOrWhiteSpace(tree.StandardError)
                    ? "reading the canonical claims tree failed"
                    : tree.StandardError.Trim());
        }

        var claimPaths = tree.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.StartsWith(ClaimCommand.ClaimsDirectory + "/", StringComparison.Ordinal))
            .ToArray();
        var storeConfigured = claimPaths.Length > 0;
        if (!storeConfigured)
        {
            // G766: an empty canonical claims tree is not enough to infer that
            // the host never adopted claims. A configured metadata branch is
            // an additional read-only adoption signal, but it never supplies
            // ownership for ordinary verification.
            var metadataConfiguration = ReadConfiguredMetadataBranch(repoRoot);
            if (metadataConfiguration.Error is not null)
            {
                return CanonicalClaimEvidence.Unavailable(metadataConfiguration.Error);
            }

            var metadataBranch = metadataConfiguration.Branch;
            if (metadataBranch is not null
                && !string.Equals(metadataBranch, canonicalBranch.Name, StringComparison.Ordinal))
            {
                return ReadMetadataBranchEvidence(repoRoot, metadataBranch);
            }

            return CanonicalClaimEvidence.NoStore;
        }

        var claimPath = ClaimCommand.ClaimPath(scope);
        if (!claimPaths.Contains(claimPath, StringComparer.Ordinal))
        {
            return CanonicalClaimEvidence.Unheld;
        }

        var show = RunGit(repoRoot, ["show", $"{remoteRef}:{claimPath}"]);
        if (show.ExitCode != 0)
        {
            return CanonicalClaimEvidence.Unavailable(
                string.IsNullOrWhiteSpace(show.StandardError)
                    ? "reading the canonical active claim failed"
                    : show.StandardError.Trim());
        }

        return new CanonicalClaimEvidence(true, true, false, show.StandardOutput, "fresh origin record");
    }

    private static CanonicalClaimEvidence ReadMetadataBranchEvidence(
        string repoRoot,
        string metadataBranch)
    {
        if (!IsSafeRemoteBranch(metadataBranch))
        {
            return CanonicalClaimEvidence.Unavailable(
                $"configured metadata branch '{metadataBranch}' is not a safe remote branch name");
        }

        var remoteRef = $"refs/remotes/origin/{metadataBranch}";
        var fetch = RunGit(repoRoot,
            ["fetch", "--quiet", "origin", $"+refs/heads/{metadataBranch}:{remoteRef}"]);
        if (fetch.ExitCode != 0)
        {
            return CanonicalClaimEvidence.Unavailable(
                string.IsNullOrWhiteSpace(fetch.StandardError)
                    ? $"fetching configured metadata branch '{metadataBranch}' failed"
                    : fetch.StandardError.Trim());
        }

        var tree = RunGit(repoRoot,
            ["ls-tree", "-r", "--name-only", remoteRef, "--", ClaimCommand.ClaimsDirectory]);
        if (tree.ExitCode != 0)
        {
            return CanonicalClaimEvidence.Unavailable(
                string.IsNullOrWhiteSpace(tree.StandardError)
                    ? $"reading configured metadata branch '{metadataBranch}' claims tree failed"
                    : tree.StandardError.Trim());
        }

        var claimCount = tree.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(IsActiveClaimPath);
        return claimCount == 0
            ? CanonicalClaimEvidence.NoStore
            : CanonicalClaimEvidence.MetadataOnly(
                metadataBranch,
                claimCount);
    }

    private static (string? Branch, string? Error) ReadConfiguredMetadataBranch(string repoRoot)
    {
        var configPath = CliRuntimeContracts.GetConfigPath(repoRoot);
        if (!File.Exists(configPath)) return (null, null);

        try
        {
            var project = CliConfigLoader.LoadFromFile(configPath).Project;
            foreach (var candidate in new[]
            {
                project.MetadataSourceBranch,
                project.MetadataBranch,
                project.MetadataWriteBranch,
            })
            {
                if (!string.IsNullOrWhiteSpace(candidate)) return (candidate.Trim(), null);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return (
                null,
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "reading configured metadata-branch settings failed"
                    : $"reading configured metadata-branch settings failed: {exception.Message}");
        }

        return (null, null);
    }

    private static bool IsActiveClaimPath(string path)
    {
        var prefix = ClaimCommand.ClaimsDirectory + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var file = path[prefix.Length..];
        return file.EndsWith(".json", StringComparison.Ordinal)
            && !file.Contains('/', StringComparison.Ordinal);
    }

    private static bool IsSafeRemoteBranch(string value) =>
        value.Length > 0
        && !value.StartsWith("-", StringComparison.Ordinal)
        && !value.StartsWith("/", StringComparison.Ordinal)
        && !value.EndsWith("/", StringComparison.Ordinal)
        && !value.EndsWith(".", StringComparison.Ordinal)
        && !value.Contains("..", StringComparison.Ordinal)
        && !value.Contains("//", StringComparison.Ordinal)
        && !value.Contains("@{", StringComparison.Ordinal)
        && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)
            || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\')
        && value.Split('/').All(segment => segment is not ("" or "." or ".."));

    private static CanonicalClaimEvidence ReadLocalEvidence(string repoRoot, string scope)
    {
        var storePath = Path.Combine(
            repoRoot, ClaimCommand.ClaimsDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(storePath)) return CanonicalClaimEvidence.NoStore;

        var claimPath = Path.Combine(
            repoRoot, ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(claimPath)) return CanonicalClaimEvidence.Unheld;
        try
        {
            return new CanonicalClaimEvidence(true, true, false, File.ReadAllText(claimPath), "local fixture record");
        }
        catch (IOException exception)
        {
            return CanonicalClaimEvidence.Unavailable(exception.Message);
        }
    }

    private static GitResult RunGit(string workdir, IReadOnlyList<string> arguments)
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
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return new GitResult(1, string.Empty, "failed to start git");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new GitResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return new GitResult(1, string.Empty, exception.Message);
        }
    }

    private static ClaimOwnershipVerification Refused(
        string status,
        string scope,
        string? invokingTeam,
        string? holder,
        string? holderTeam,
        string detail,
        bool storeConfigured = true) =>
        new(
            Passed: false,
            Status: status,
            Scope: scope,
            StoreConfigured: storeConfigured,
            InvokingTeam: invokingTeam,
            Holder: holder,
            HolderTeam: holderTeam,
            Detail: detail);

    private sealed record CanonicalClaimEvidence(
        bool Available,
        bool StoreConfigured,
        bool MetadataBranchOnly,
        string? RecordJson,
        string Detail)
    {
        public static CanonicalClaimEvidence NoStore { get; } =
            new(true, false, false, null, "canonical claims store absent");
        public static CanonicalClaimEvidence Unheld { get; } =
            new(true, true, false, null, "canonical scope unheld");
        public static CanonicalClaimEvidence Unavailable(string detail) =>
            new(false, false, false, null, detail);
        public static CanonicalClaimEvidence MetadataOnly(string metadataBranch, int claimCount) =>
            new(
                true,
                true,
                true,
                null,
                $"Claims store is configured on metadata branch '{metadataBranch}' with {claimCount} active record(s), but the canonical branch has no claim records; refusing to treat every scope as unconfigured.");
    }

    private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);
}

internal sealed record ClaimStoreProbe(bool Available, bool StoreConfigured, string Detail);

internal static class ClaimVerificationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && args[0] == "--help")
        {
            WriteHelp(writer);
            return 0;
        }

        string? scope = null;
        string? team = null;
        var format = "json";
        for (var index = 0; index < args.Length; index++)
        {
            string? NextValue(string option)
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    writer.WriteLine($"{option} requires a value.");
                    return null;
                }
                return args[++index];
            }

            switch (args[index])
            {
                case "--scope": scope = NextValue("--scope"); break;
                case "--team": team = NextValue("--team"); break;
                case "--format": format = NextValue("--format") ?? format; break;
                default:
                    writer.WriteLine($"Unknown argument '{args[index]}'.");
                    WriteHelp(writer);
                    return 1;
            }
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            writer.WriteLine("--scope is required.");
            WriteHelp(writer);
            return 1;
        }
        if (format is not "json" and not "markdown")
        {
            writer.WriteLine("--format must be json or markdown.");
            return 1;
        }

        var result = ClaimOwnershipVerifier.Verify(context.RepoRoot, scope, team);
        Write(writer, format, result);
        return result.Passed ? 0 : 1;
    }

    internal static void Write(TextWriter writer, string format, ClaimOwnershipVerification result)
    {
        if (string.Equals(format, "json", StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine("# Claim verification (G680 — preview-through-1.x)");
        writer.WriteLine();
        writer.WriteLine($"- status: {result.Status}");
        writer.WriteLine($"- passed: {result.Passed.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- scope: {result.Scope}");
        writer.WriteLine($"- invoking team: {result.InvokingTeam ?? "(unspecified)"}");
        writer.WriteLine($"- holder: {result.Holder ?? "(none)"}");
        writer.WriteLine($"- holder team: {result.HolderTeam ?? "(none)"}");
        writer.WriteLine($"- detail: {result.Detail}");
    }

    private static void WriteHelp(TextWriter writer) =>
        writer.WriteLine("Usage: intent-cli claim verify --scope <execution-unit:EU|release-prep:owner/repo:version> [--team <team>] [--format json|markdown]");
}

internal sealed record ClaimOwnershipVerification(
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("store_configured")] bool StoreConfigured,
    [property: JsonPropertyName("invoking_team")] string? InvokingTeam,
    [property: JsonPropertyName("holder")] string? Holder,
    [property: JsonPropertyName("holder_team")] string? HolderTeam,
    [property: JsonPropertyName("detail")] string Detail)
{
    public const string StatusNotConfigured = "not-configured";
    public const string StatusOwned = "owned";
    public const string StatusUnheldAvailable = "unheld-available";
    public const string StatusUnheld = "unheld";
    public const string StatusHeldByOtherTeam = "held-by-other-team";
    public const string StatusTeamRequired = "team-required";
    public const string StatusInvalid = "invalid";
    public const string StatusCanonicalUnavailable = "canonical-unavailable";
    public const string StatusMetadataBranchOnly = "metadata-branch-only";
}
