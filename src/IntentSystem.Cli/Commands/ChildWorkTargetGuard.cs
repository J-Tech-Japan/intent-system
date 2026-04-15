namespace IntentSystem.Cli.Commands;

internal static class ChildWorkTargetGuard
{
    private const string HostRuntimeSegment = ".intent-cli";

    public static void EnsureTargetAllowed(
        string executionUnit,
        string hostRepoRootPath,
        string targetRepo,
        string checkoutRootPath,
        string targetPath,
        string targetPart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostRepoRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPart);

        if (ContainsHostRuntimeSegment(targetRepo))
        {
            throw new InvalidOperationException(
                $"Child target repo '{targetRepo}' for '{executionUnit}' points to host runtime-only '.intent-cli/**' content. Parent-side clarification is required before generating or launching child work.");
        }

        var normalizedHostRepoRoot = Path.GetFullPath(hostRepoRootPath);
        var resolvedTargetRepo = ResolvePath(normalizedHostRepoRoot, targetRepo);
        if (IsWithinRoot(
                ResolvePath(normalizedHostRepoRoot, HostRuntimeSegment),
                resolvedTargetRepo))
        {
            throw new InvalidOperationException(
                $"Child target repo '{targetRepo}' for '{executionUnit}' resolves into host runtime-only '.intent-cli/**' content. Parent-side clarification is required before generating or launching child work.");
        }

        if (ContainsHostRuntimeSegment(targetPath))
        {
            throw new InvalidOperationException(
                $"Child target path '{targetPath}' for '{executionUnit}' points to host runtime-only '.intent-cli/**' content. Parent-side clarification is required before generating or launching child work.");
        }

        if (ContainsHostRuntimeSegment(targetPart))
        {
            throw new InvalidOperationException(
                $"Child target part '{targetPart}' for '{executionUnit}' points to host runtime-only '.intent-cli/**' content. Parent-side clarification is required before generating or launching child work.");
        }

        var normalizedCheckoutRoot = Path.GetFullPath(checkoutRootPath);
        var resolvedTargetPath = ResolvePath(normalizedCheckoutRoot, targetPath);
        if (!IsWithinRoot(normalizedCheckoutRoot, resolvedTargetPath))
        {
            throw new InvalidOperationException(
                $"Child target path '{targetPath}' for '{executionUnit}' resolves outside the child checkout root '{normalizedCheckoutRoot}'. Parent-side clarification is required before generating or launching child work.");
        }

        if (!LooksLikePath(targetPart))
        {
            return;
        }

        var resolvedTargetPart = ResolvePath(resolvedTargetPath, targetPart);
        if (!IsWithinRoot(normalizedCheckoutRoot, resolvedTargetPart))
        {
            throw new InvalidOperationException(
                $"Child target part '{targetPart}' for '{executionUnit}' resolves outside the child checkout root '{normalizedCheckoutRoot}' when combined with target path '{targetPath}'. Parent-side clarification is required before generating or launching child work.");
        }
    }

    private static bool ContainsHostRuntimeSegment(string value)
    {
        var segments = value.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Contains(HostRuntimeSegment, StringComparer.Ordinal);
    }

    private static bool LooksLikePath(string value)
    {
        return value.StartsWith(".", StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal);
    }

    private static string ResolvePath(string rootPath, string relativeOrAbsolutePath)
    {
        return Path.GetFullPath(Path.Combine(
            rootPath,
            relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar);

        return string.Equals(candidatePath, normalizedRoot, comparison)
            || candidatePath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
