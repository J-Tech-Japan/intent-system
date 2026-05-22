using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G385: pure, process-free boundary that hardens parsing of <c>gh issue
/// list</c> / <c>gh pr list</c> JSON for the github-only worker selector
/// (<c>intent-cli worker next-action --github-only</c>).
///
/// Why this exists: a child implementation loop observed
/// <c>could not parse gh issue list ... JSON: 'u' is invalid after a value
/// ... BytePositionInLine: 366</c> — a raw <see cref="JsonException"/> leaking
/// to the operator because <c>gh</c> stdout was contaminated with trailing
/// non-JSON text (an update notice / warning printed alongside the array,
/// likely under Windows PowerShell native-command capture). A raw parser
/// exception is poor operator guidance.
///
/// This boundary:
/// - parses only the stdout JSON stream (callers already capture stdout and
///   stderr separately);
/// - normalizes legitimate encoding artifacts (a leading UTF-8 BOM and
///   surrounding whitespace) so otherwise-valid JSON still parses;
/// - applies a clear refusal when stdout is not exactly a valid JSON array:
///   any leading/trailing contamination (update notices, progress/warning
///   lines printed to stdout, trailing native-command text) yields a
///   structured <c>github-json-invalid</c> diagnostic (classification +
///   repo/command family + byte/line if available + a redacted output preview
///   + a recommended retry/preflight) instead of a raw exception. Silent
///   best-effort recovery is intentionally avoided so a contaminated
///   environment is surfaced to the operator, not masked;
/// - classifies a non-zero <c>gh</c> exit into <c>github-auth-failed</c> vs
///   <c>github-command-failed</c>.
///
/// No I/O, no <c>Process.Start</c> — the lister adapter supplies the captured
/// stdout/stderr/exit code, so every branch is unit-testable with fixtures
/// (including the JTJ_Estivo byte-366 error shape and Windows PowerShell
/// output contamination).
/// </summary>
internal static partial class GitHubCliJsonBoundary
{
    /// <summary>
    /// Stable classifications surfaced to the operator/agent. <c>valid</c> is
    /// the success case; the others are the github-only failure family.
    /// (<c>no-actionable-target</c> / <c>ambiguous-target</c> are selection
    /// outcomes owned by the analyzer layer, not this parser boundary.)
    /// </summary>
    public static class Classifications
    {
        public const string Valid = "valid";
        public const string GithubJsonInvalid = "github-json-invalid";
        public const string GithubCommandFailed = "github-command-failed";
        public const string GithubAuthFailed = "github-auth-failed";
    }

    private const int DefaultPreviewLength = 240;

    /// <summary>
    /// Extract the JSON array payload from possibly-contaminated <c>gh</c>
    /// stdout. Returns a successful extraction (with the cleaned JSON ready to
    /// deserialize) or a structured <c>github-json-invalid</c> diagnostic.
    /// </summary>
    /// <param name="stdout">Raw captured <c>gh</c> stdout (may include a BOM or surrounding non-JSON text).</param>
    /// <param name="callDescription">Human-readable call descriptor, e.g. <c>`gh issue list` for owner/repo</c>; carries the gh command family and repo into the diagnostic.</param>
    public static GitHubCliJsonExtraction ExtractJsonArray(string? stdout, string callDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callDescription);

        var raw = stdout ?? string.Empty;
        // PowerShell / native-command capture can prepend a UTF-8 BOM.
        if (raw.Length > 0 && raw[0] == '\uFEFF')
        {
            raw = raw[1..];
        }

        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return Invalid(
                callDescription,
                "gh produced no stdout (empty output)",
                errorByte: null,
                errorLine: null,
                preview: string.Empty);
        }

        // The whole trimmed payload (after BOM strip) must be exactly a valid
        // JSON array. This is the clean macOS/zsh case and preserves the
        // pre-G385 result for legitimate output.
        if (TryValidateJsonArray(trimmed, out var parseError))
        {
            return new GitHubCliJsonExtraction
            {
                Succeeded = true,
                Classification = Classifications.Valid,
                Json = trimmed,
            };
        }

        // Anything else — trailing update notices, leading warnings, or other
        // native-command contamination — is refused with a structured,
        // sanitized diagnostic (byte/line position if the JSON parser reported
        // one) rather than a raw parser exception.
        return Invalid(
            callDescription,
            "gh stdout was not valid JSON",
            errorByte: parseError?.BytePositionInLine,
            errorLine: parseError?.LineNumber,
            preview: trimmed);
    }

    /// <summary>
    /// Classify a non-zero <c>gh</c> exit into <c>github-auth-failed</c> (when
    /// the captured output mentions an authentication problem) vs the generic
    /// <c>github-command-failed</c>.
    /// </summary>
    public static string ClassifyProcessFailure(string? stderr, string? stdout)
    {
        var combined = (stderr ?? string.Empty) + "\n" + (stdout ?? string.Empty);
        return AuthFailureRegex().IsMatch(combined)
            ? Classifications.GithubAuthFailed
            : Classifications.GithubCommandFailed;
    }

    /// <summary>
    /// Sanitize an output snippet for safe inclusion in a diagnostic: collapse
    /// whitespace/newlines to a single line, redact token-like substrings
    /// (GitHub PATs, <c>Bearer</c> tokens), and cap the length.
    /// </summary>
    public static string SanitizePreview(string? raw, int maxLength = DefaultPreviewLength)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var collapsed = WhitespaceRegex().Replace(raw, " ").Trim();
        var redacted = TokenRegex().Replace(collapsed, "***redacted***");
        return redacted.Length > maxLength
            ? redacted[..maxLength] + "…(truncated)"
            : redacted;
    }

    private static GitHubCliJsonExtraction Invalid(
        string callDescription,
        string summary,
        long? errorByte,
        long? errorLine,
        string preview)
    {
        var sanitized = SanitizePreview(preview);
        var location = errorLine is { } line && errorByte is { } position
            ? $" (line {line}, byte {position})"
            : string.Empty;
        var previewClause = sanitized.Length == 0
            ? string.Empty
            : $" sanitized preview: \"{sanitized}\".";

        var message =
            $"could not parse {callDescription}: {summary}{location}.{previewClause}"
            + " Recommended: inspect raw output with `gh issue list --repo <owner/repo>"
            + " --json number,title,labels,url`, ensure no shell-profile/update-notice/warning"
            + " text contaminates stdout (especially under Windows PowerShell), then retry"
            + " `intent-cli worker next-action --github-only`.";

        return new GitHubCliJsonExtraction
        {
            Succeeded = false,
            Classification = Classifications.GithubJsonInvalid,
            DiagnosticMessage = message,
            ErrorByteOffset = errorByte,
            ErrorLineNumber = errorLine,
            Preview = sanitized,
            RecommendedAction = "inspect-gh-output-and-retry",
        };
    }

    private static bool TryValidateJsonArray(string candidate, out JsonException? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(candidate);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException exception)
        {
            error = exception;
            return false;
        }
    }

    [GeneratedRegex(
        @"gh auth login|not logged in|authentication failed|requires authentication|bad credentials|unauthorized|http 401|\b401\b|token has expired|invalid token|no authentication token",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthFailureRegex();

    [GeneratedRegex(
        @"gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|Bearer\s+[A-Za-z0-9._\-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}

/// <summary>
/// G385: the verdict from <see cref="GitHubCliJsonBoundary.ExtractJsonArray"/>.
/// On success, <see cref="Json"/> is a validated JSON array ready to
/// deserialize; on failure, <see cref="DiagnosticMessage"/> carries the
/// sanitized structured diagnostic.
/// </summary>
internal sealed record GitHubCliJsonExtraction
{
    public required bool Succeeded { get; init; }

    public required string Classification { get; init; }

    /// <summary>The validated JSON array payload to deserialize (success only).</summary>
    public string Json { get; init; } = "[]";

    /// <summary>Operator-facing, sanitized diagnostic with a recommended action (failure only).</summary>
    public string DiagnosticMessage { get; init; } = string.Empty;

    public long? ErrorByteOffset { get; init; }

    public long? ErrorLineNumber { get; init; }

    /// <summary>Redacted, length-capped preview of the offending output (failure only).</summary>
    public string Preview { get; init; } = string.Empty;

    public string RecommendedAction { get; init; } = string.Empty;
}
