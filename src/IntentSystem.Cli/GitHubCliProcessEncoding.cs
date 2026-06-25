using System.Text;

namespace IntentSystem.Cli;

/// <summary>
/// G484: shared decoding contract for <c>gh</c> subprocess output. GitHub CLI
/// always emits UTF-8 (JSON and error text), but when a redirected
/// <see cref="System.Diagnostics.ProcessStartInfo"/> does not pin
/// <c>StandardOutputEncoding</c> / <c>StandardErrorEncoding</c>, .NET decodes
/// the streams with <see cref="System.Console.OutputEncoding"/> — which on a
/// Japanese Windows console defaults to cp932/OEM. That mis-decodes multi-byte
/// UTF-8 sequences in Japanese issue titles/bodies and corrupts otherwise-valid
/// <c>gh</c> JSON, breaking <c>worker next-action</c> / preflight selection.
///
/// Every <c>gh</c> invocation pins both stream encodings to
/// <see cref="Utf8NoBom"/> so decoding is UTF-8 regardless of the ambient
/// console code page, Git Bash, or PowerShell host encoding. This is identical
/// to existing behavior on macOS/Linux (whose console is already UTF-8), so the
/// fix is a no-op there and only repairs the Windows cp932 path.
/// </summary>
internal static class GitHubCliProcessEncoding
{
    /// <summary>
    /// UTF-8 without a byte-order mark. Used as both
    /// <c>StandardOutputEncoding</c> and <c>StandardErrorEncoding</c> for every
    /// <c>gh</c> subprocess so JSON and diagnostics decode as UTF-8.
    /// </summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
