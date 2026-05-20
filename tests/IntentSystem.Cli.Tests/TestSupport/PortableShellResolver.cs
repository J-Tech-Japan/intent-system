namespace IntentSystem.Cli.Tests.TestSupport;

/// <summary>
/// G370: portable shell discovery for test fixtures that launch child
/// processes through a login-shell wrapper. The historical hard-coded
/// path was <c>/bin/zsh</c>, which works on macOS dev machines but
/// breaks on GitHub-hosted Ubuntu runners (no <c>/bin/zsh</c>) and any
/// container that ships with bash only. This helper inspects the
/// runtime environment and returns the first existing absolute path
/// from a small priority list, falling back to <c>/bin/sh</c> which is
/// guaranteed on any POSIX-compliant runner.
///
/// The helper intentionally avoids <see cref="System.Environment.GetEnvironmentVariable"/>
/// lookups on <c>SHELL</c> -- CI containers often run with <c>SHELL</c>
/// unset, and we want a deterministic, byte-identical resolution
/// regardless of operator profile.
/// </summary>
internal static class PortableShellResolver
{
    /// <summary>
    /// Ordered candidates: zsh first to preserve the macOS dev-loop
    /// fixture behavior (zsh-only features such as <c>-lc</c> with
    /// glob expansion match the original tests bit-for-bit), then
    /// bash variants for Linux CI, then POSIX <c>sh</c> as the
    /// universal fallback.
    /// </summary>
    private static readonly string[] Candidates =
    {
        "/bin/zsh",
        "/bin/bash",
        "/usr/bin/bash",
        "/usr/local/bin/bash",
        "/bin/sh",
    };

    /// <summary>
    /// Resolve the first available shell on the runner. Throws
    /// <see cref="InvalidOperationException"/> if none of the
    /// candidates exist -- which would only happen on a runner so
    /// minimal it cannot host these integration fixtures at all.
    /// </summary>
    public static string Resolve()
    {
        foreach (var candidate in Candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"PortableShellResolver: no candidate shell found in [{string.Join(", ", Candidates)}].");
    }
}
