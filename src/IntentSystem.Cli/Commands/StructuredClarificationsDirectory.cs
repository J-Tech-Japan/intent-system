namespace IntentSystem.Cli.Commands;

/// <summary>
/// G302: scans <c>intents/&lt;domain&gt;/clarifications/*.toml</c> and returns
/// the parsed <see cref="StructuredClarification"/> records. The legacy
/// <c>open.md</c> markdown file is intentionally ignored — that path is
/// handled by <see cref="ClarificationOpenDetector"/> and the structured
/// directory adds an additive source of truth.
/// </summary>
internal static class StructuredClarificationsDirectory
{
    public static string ResolveDirectory(string repoRoot, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        return Path.Combine(repoRoot, "intents", domain, "clarifications");
    }

    public static string ResolveFile(string repoRoot, string domain, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Path.Combine(ResolveDirectory(repoRoot, domain), $"{id}.toml");
    }

    public static IReadOnlyList<StructuredClarification> ReadAll(string repoRoot, string domain)
    {
        var directory = ResolveDirectory(repoRoot, domain);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<StructuredClarification>();
        }

        var results = new List<StructuredClarification>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.toml")
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            var toml = File.ReadAllText(path);
            try
            {
                var clarification = StructuredClarificationToml.Deserialize(toml, sourcePath: path);
                results.Add(clarification);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"Failed to parse structured clarification at '{path}': {exception.Message}", exception);
            }
        }

        return results;
    }

    public static bool HasOpenBlocker(string repoRoot, string domain)
    {
        return ReadAll(repoRoot, domain).Any(c => c.IsOpen());
    }
}
