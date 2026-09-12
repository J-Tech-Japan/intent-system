using System.Security.Cryptography;
using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only source facts attached to a team-scoped host-loop request.
/// Dispatch identity is deliberately not invented here: callers must supply
/// the authoritative task/result nonce and, when a qualified route is
/// required, an immutable generation and digest through the testable seam.
/// </summary>
internal sealed record HostLoopIdentityCapture
{
    public string? Cwd { get; init; }
    public string? Origin { get; init; }
    public string? Ref { get; init; }
    public string? Head { get; init; }
    public string? DispatchGeneration { get; init; }
    public string? DispatchDigest { get; init; }
    public string? RecipientContext { get; init; }
    public string? Owner { get; init; }
    public string? Action { get; init; }
    public string? Deadline { get; init; }

    /// <summary>
    /// Capture local source facts without reading queue, claims, packets, or
    /// any other durable host state. A bounded non-interactive Git runner is
    /// used so a remote credential prompt cannot hold the host loop.
    /// </summary>
    public static HostLoopIdentityCapture Capture(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cwd = Directory.GetCurrentDirectory();
        var runner = new CheckoutFreshnessGitCommandRunner(TimeSpan.FromSeconds(2));
        var origin = ReadGit(runner, context.RepoRoot, ["remote", "get-url", "origin"]);
        var reference = ReadGit(runner, context.RepoRoot, ["symbolic-ref", "--quiet", "--short", "HEAD"]);
        var head = ReadGit(runner, context.RepoRoot, ["rev-parse", "HEAD"]);

        return new HostLoopIdentityCapture
        {
            Cwd = cwd,
            Origin = origin,
            Ref = reference,
            Head = head,
        };
    }

    internal static string ComputeDigest(
        string repo,
        string? domain,
        string? team,
        string? taskId,
        string? resultNonce,
        HostLoopIdentityCapture capture)
    {
        var canonical = string.Join("\n", [
            repo,
            domain ?? string.Empty,
            team ?? string.Empty,
            taskId ?? string.Empty,
            resultNonce ?? string.Empty,
            capture.DispatchGeneration ?? string.Empty,
            capture.Cwd ?? string.Empty,
            capture.Origin ?? string.Empty,
            capture.Ref ?? string.Empty,
            capture.Head ?? string.Empty,
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string? ReadGit(
        IGitRemoteCommandRunner runner,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        try
        {
            var result = runner.Run(workingDirectory, arguments);
            if (result.ExitCode == 0 && !result.TimedOut)
            {
                var value = result.StdOut.Trim();
                return value.Length == 0 ? null : value;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // Source capture is an observation. The command reports the
            // missing fact as identity-unresolved instead of guessing.
        }

        return null;
    }
}
