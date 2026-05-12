namespace IntentSystem.Cli.Commands;

/// <summary>
/// G338: shared argument-forwarding helpers for the loop-flavored
/// <c>guide workflow task</c> wrappers (implementation-loop /
/// review-next-slice-loop). Each task name pins a specific
/// <c>--mode</c> on <see cref="GuidePromptMatrixCommand"/>; this
/// helper prepends the mode, detects the operator-supplied
/// <c>--mode</c> we explicitly forbid, and validates that every
/// flag the operator passed belongs to the documented task
/// surface (so unknown-arg errors surface the TASK usage line, not
/// the underlying prompt-matrix usage line).
/// </summary>
internal static class GuideWorkflowTaskLoopForwarder
{
    /// <summary>
    /// Flags both task wrappers accept and forward verbatim to
    /// <see cref="GuidePromptMatrixCommand"/>. Mirror exactly the
    /// `--target-repo` / `--agent` / `--frequency` /
    /// `--base-branch-policy` / `--domain` / `--format` set that
    /// the underlying prompt-matrix parser knows about. Listed as
    /// an explicit allow-list so an unknown flag is rejected with
    /// the wrapper's own usage line (not the prompt-matrix one).
    /// </summary>
    internal static readonly IReadOnlySet<string> AllowedFlags = new HashSet<string>(StringComparer.Ordinal)
    {
        "--target-repo",
        "--agent",
        "--frequency",
        "--base-branch-policy",
        "--domain",
        "--format",
        "--help"
    };

    /// <summary>
    /// Walks the argument list and returns the first flag-shaped
    /// token (starts with <c>--</c>) that is not in
    /// <see cref="AllowedFlags"/>. Returns <see langword="null"/>
    /// when every flag is recognized. Values that follow a flag
    /// (e.g. <c>--target-repo example/repo</c>) are not validated
    /// here; the underlying prompt-matrix parser owns value
    /// validation.
    /// </summary>
    public static string? FindFirstUnknownFlag(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                // Positional tokens are not currently expected; let
                // the downstream parser reject them so the message
                // stays consistent.
                continue;
            }
            if (!AllowedFlags.Contains(arg))
            {
                return arg;
            }
            // Skip the value when the flag takes one (every flag in
            // the allow-list except --help takes a value).
            if (!string.Equals(arg, "--help", StringComparison.Ordinal) && index + 1 < args.Length)
            {
                index++;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the original argument list with <c>--mode &lt;mode&gt;</c>
    /// prepended. The wrappers reject an operator-supplied
    /// <c>--mode</c> via <see cref="HasFlag"/> before reaching this
    /// helper, so the caller can rely on the forwarded array carrying
    /// exactly one mode value.
    /// </summary>
    public static string[] PrependMode(string[] args, string mode)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mode);

        var forwarded = new string[args.Length + 2];
        forwarded[0] = "--mode";
        forwarded[1] = mode;
        Array.Copy(args, 0, forwarded, 2, args.Length);
        return forwarded;
    }

    /// <summary>
    /// Checks whether the supplied argument list contains a specific
    /// flag literal. Used to fail-closed when the operator tries to
    /// override <c>--mode</c> through a task wrapper that has already
    /// pinned the mode.
    /// </summary>
    public static bool HasFlag(string[] args, string flag)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(flag);

        foreach (var arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
