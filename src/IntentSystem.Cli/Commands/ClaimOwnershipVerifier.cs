using System.Text.Json;
using System.Text.Json.Serialization;

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

        var storePath = Path.Combine(repoRoot, ClaimCommand.ClaimsDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(storePath))
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

        if (!ClaimCommand.TryValidateScope(scope, out var scopeError))
        {
            return Refused(
                ClaimOwnershipVerification.StatusInvalid,
                scope,
                invokingTeam,
                null,
                null,
                $"claim verification refused scope '{scope}': {scopeError}");
        }

        var claimPath = Path.Combine(repoRoot, ClaimCommand.ClaimPath(scope).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(claimPath))
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
            record = JsonSerializer.Deserialize<ClaimRecord>(File.ReadAllText(claimPath), JsonOptions);
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
        catch (IOException exception)
        {
            return Refused(
                ClaimOwnershipVerification.StatusInvalid,
                scope,
                invokingTeam,
                null,
                null,
                $"claim verification refused scope '{scope}': active record could not be read ({exception.Message}).");
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

    private static ClaimOwnershipVerification Refused(
        string status,
        string scope,
        string? invokingTeam,
        string? holder,
        string? holderTeam,
        string detail) =>
        new(
            Passed: false,
            Status: status,
            Scope: scope,
            StoreConfigured: true,
            InvokingTeam: invokingTeam,
            Holder: holder,
            HolderTeam: holderTeam,
            Detail: detail);
}

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
}
