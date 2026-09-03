namespace IntentSystem.Cli.Commands;

/// <summary>
/// G791: the one authority for deciding whether a repository-relative path is
/// durable host state. The workspace guard, host-sync preflight, and durable
/// state preflight must agree on this boundary: a path cannot be unsafe to
/// stash in one command and invisible to the command that names the recovery.
/// </summary>
internal static class DurableHostStatePathClassifier
{
    public static bool IsDurableHostStatePath(string? path)
    {
        var normalized = NormalizePath(path);
        return normalized.Equals(".intent-cli", StringComparison.Ordinal)
            || normalized.StartsWith(".intent-cli/", StringComparison.Ordinal)
            || normalized.Equals("intents", StringComparison.Ordinal)
            || normalized.StartsWith("intents/", StringComparison.Ordinal)
            || normalized.Equals("AGENTS.md", StringComparison.Ordinal)
            || normalized.Equals("CLAUDE.md", StringComparison.Ordinal);
    }

    public static bool IsDurableHostStateParent(string? path)
    {
        var normalized = NormalizePath(path).TrimEnd('/');
        return normalized.Equals(".intent-cli", StringComparison.Ordinal)
            || normalized.StartsWith(".intent-cli/", StringComparison.Ordinal)
            || normalized.Equals("intents", StringComparison.Ordinal)
            || normalized.StartsWith("intents/", StringComparison.Ordinal);
    }

    public static string NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').TrimStart('/');
}
