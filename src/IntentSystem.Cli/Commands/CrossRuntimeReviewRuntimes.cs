namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834: the supported reviewer runtimes (ordinal) and the pinned, read-only
/// invocation text the seat runs for each. intent-cli renders this text only; it
/// never starts, launches, or manages any agent or provider process.
/// </summary>
internal static class CrossRuntimeReviewRuntimes
{
    public const string Codex = "codex";
    public const string Claude = "claude";
    public const string Cursor = "cursor";

    public static readonly IReadOnlyList<string> All = [Codex, Claude, Cursor];

    public static bool IsSupported(string? runtime) =>
        runtime is not null && All.Contains(runtime, StringComparer.Ordinal);

    public static string Describe() => "codex, claude, or cursor";

    /// <summary>
    /// The executable plus every flag and flag value each pinned invocation may
    /// carry. A token that starts with <c>-</c> and is not listed here is a
    /// contract violation (tests tokenize every rendered invocation against it).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedFlags =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Codex] = ["-s", "-C", "--output-schema", "-o", "-"],
            [Claude] = ["-p", "--permission-mode", "--disallowedTools", "--output-format", "--json-schema"],
            [Cursor] = ["-p", "--mode", "--sandbox", "--trust", "--workspace", "--output-format"],
        };

    /// <summary>
    /// What each pinned invocation actually enforces, as measured on 2026-09-14
    /// (G834 real runs and probes). Guides and the request output repeat these
    /// statements so no seat assumes more isolation than a runtime provides.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ReadOnlyEnforcement =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Codex] = "codex `-s read-only` is sandbox-enforced: the reviewer can read and run commands, but the sandbox refuses file writes.",
            [Claude] = "claude `--permission-mode plan --disallowedTools Edit,Write,NotebookEdit` removes the file-writing tools only. Command execution (for example building and running tests) is allowed, and writes made through shell commands are not sandbox-enforced.",
            [Cursor] = "cursor `--mode ask` refuses every non-read-only tool, including all shell commands, so the reviewer reads files but cannot run git or tests; the head it echoes is read from files such as `.git/HEAD`, not checked with git. `--mode plan` was not used because a measured plan-mode run switched itself to agent mode and wrote files inside and outside the workspace; `--sandbox enabled` did not stop those writes.",
        };

    /// <summary>The first line of <c>invocation.txt</c>: who runs it.</summary>
    public static string InvocationLabel(string runtime) =>
        $"# run by the seat ({runtime}); intent-cli renders this text and never runs it";

    /// <summary>
    /// Renders the pinned invocation for <paramref name="runtime"/>. Every
    /// interpolated path is POSIX single-quoted by
    /// <see cref="CrossRuntimeReviewPaths.ShellQuote"/>.
    /// </summary>
    public static string RenderInvocation(string runtime, string clone, string outDir)
    {
        var quotedClone = CrossRuntimeReviewPaths.ShellQuote(clone);
        string Out(string file) => CrossRuntimeReviewPaths.ShellQuote(Path.Combine(outDir, file));

        return runtime switch
        {
            Codex =>
                $"codex exec -s read-only -C {quotedClone} --output-schema {Out(CrossRuntimeReviewFiles.Schema)} "
                + $"-o {Out(CrossRuntimeReviewFiles.RawVerdict)} - < {Out(CrossRuntimeReviewFiles.Prompt)}",
            Claude =>
                $"cd {quotedClone} && claude -p --permission-mode plan --disallowedTools Edit,Write,NotebookEdit --output-format json "
                + $"--json-schema \"$(cat {Out(CrossRuntimeReviewFiles.Schema)})\" < {Out(CrossRuntimeReviewFiles.Prompt)} "
                + $"> {Out(CrossRuntimeReviewFiles.RawVerdict)}",
            Cursor =>
                $"cursor-agent -p --mode ask --sandbox enabled --trust --workspace {quotedClone} --output-format json "
                + $"\"$(cat {Out(CrossRuntimeReviewFiles.Prompt)})\" > {Out(CrossRuntimeReviewFiles.RawVerdict)}",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported cross-runtime review runtime."),
        };
    }
}

/// <summary>G834: the file names a review request renders and a run produces.</summary>
internal static class CrossRuntimeReviewFiles
{
    public const string Prompt = "prompt.md";
    public const string Schema = "verdict.schema.json";
    public const string Invocation = "invocation.txt";
    public const string RawVerdict = "verdict.raw.json";

    public static readonly IReadOnlyList<string> Rendered = [Prompt, Schema, Invocation];
}

/// <summary>G834: every named cause the cross-runtime review surfaces emit.</summary>
internal static class CrossRuntimeReviewCauses
{
    public const string HostRootRequired = "cross-runtime-review-host-root-required";
    public const string PathInvalid = "cross-runtime-review-path-invalid";
    public const string OutDirNotEmpty = "cross-runtime-review-out-dir-not-empty";
    public const string PacketMissing = "cross-runtime-review-packet-missing";
    public const string ArgumentInvalid = "cross-runtime-review-argument-invalid";
    public const string TeamUnresolved = "cross-runtime-review-team-unresolved";
    public const string UnitMismatch = "cross-runtime-review-unit-mismatch";
    public const string NotDeclared = "cross-runtime-review-not-declared";
    public const string HeadMismatch = "cross-runtime-review-head-mismatch";
    public const string VerdictInvalid = "cross-runtime-review-verdict-invalid";
    public const string RuntimeInvalid = "cross-runtime-review-runtime-invalid";
    public const string RecordCollision = "cross-runtime-review-record-collision";
    public const string HeadSuperseded = "cross-runtime-review-head-superseded";
    public const string Blocked = "cross-runtime-review-blocked";
    public const string Missing = "cross-runtime-review-missing";
    public const string RereviewMissing = "cross-runtime-review-rereview-missing";
    public const string RecordUnreadable = "cross-runtime-review-record-unreadable";
    public const string HeadRequired = "cross-runtime-review-head-required";
    public const string HeadStale = "cross-runtime-review-head-stale";
}
