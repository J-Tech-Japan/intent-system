using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G366: testability seam for <c>gh label list</c>. The
/// <see cref="AutomationLabelPaletteAuditCommand"/> and
/// <see cref="AutomationLabelPaletteSyncCommand"/> commands use this
/// to read the live label metadata for a repository; tests inject a
/// fake to model missing labels, drifted colors, and drifted
/// descriptions deterministically without invoking <c>gh</c>.
/// </summary>
internal interface IGitHubLabelLister
{
    IReadOnlyList<GitHubLabelMetadata> ListLabels(string repo);
}

/// <summary>
/// G366: minimal projection of <c>gh label list --json
/// name,color,description</c>. Only the fields the audit/sync
/// analyzer compares against the canonical palette are surfaced;
/// other GitHub label metadata (URL, id) is intentionally excluded so
/// the seam stays stable.
/// </summary>
internal sealed record GitHubLabelMetadata
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("color")] public string Color { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
}

/// <summary>
/// G366: default lister that shells out to
/// <c>gh label list --repo &lt;owner/repo&gt; --json
/// name,color,description --limit 200</c>. The only file in the
/// label-palette surface permitted to call <c>Process.Start</c>;
/// analyzer and command layers remain pure.
/// </summary>
internal sealed class GhCliGitHubLabelLister : IGitHubLabelLister
{
    public IReadOnlyList<GitHubLabelMetadata> ListLabels(string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            StandardErrorEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "label", "list",
            "--repo", repo,
            "--json", "name,color,description",
            "--limit", "200",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        string stdout;
        string stderr;
        int exitCode;
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"failed to start `gh` process to list labels in {repo}");
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException)
        {
            throw new InvalidOperationException(
                $"could not invoke `gh` to list labels in {repo}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh` failed to list labels in {repo} with exit {exitCode}: {errorBody.Trim()}");
        }

        try
        {
            return JsonSerializer.Deserialize<List<GitHubLabelMetadata>>(stdout)
                ?? new List<GitHubLabelMetadata>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse `gh label list` for {repo} JSON: {exception.Message}",
                exception);
        }
    }
}
