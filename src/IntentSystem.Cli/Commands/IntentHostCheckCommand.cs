using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Model;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G309: <c>intent-cli intent host-check --domain &lt;d&gt;
/// [--target-repo &lt;owner/repo&gt;] [--observed-remote &lt;url&gt;]
/// [--format markdown|json]</c> — read-only smoke check that classifies
/// whether the current cwd is a canonical, fully-initialized intent
/// host for the given domain. Wraps
/// <see cref="IntentHostCheckAnalyzer"/> with the file-presence and
/// host-binding-parsing I/O.
///
/// The command captures the observed git remote via
/// <c>git remote get-url origin</c> by default; tests inject a fake
/// observed remote via <c>--observed-remote</c>. No state mutation,
/// no `gh` calls, no AI provider launch.
/// </summary>
internal static class IntentHostCheckCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    /// <summary>Test seam — replaces the default observed remote capture.</summary>
    public static Func<string, string?>? ObservedRemoteFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, context, out var parsed, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var repoRoot = context.RepoRoot;
        var configPath = Path.Combine(repoRoot, ".intent-cli", "config.toml");
        var bindingPath = Path.Combine(repoRoot, ".intent-cli", "host-binding.toml");
        var domainDir = Path.Combine(repoRoot, "intents", parsed.Domain);
        var agentsPath = Path.Combine(repoRoot, "AGENTS.md");
        var claudePath = Path.Combine(repoRoot, "CLAUDE.md");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var runsLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");

        var bindingPresent = File.Exists(bindingPath);
        string? boundHostRepo = null;
        string? bindingTargetRepo = null;
        if (bindingPresent)
        {
            try
            {
                (boundHostRepo, bindingTargetRepo) = ParseHostBinding(File.ReadAllText(bindingPath));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                writer.WriteLine($"failed to parse `.intent-cli/host-binding.toml`: {exception.Message}");
                return 1;
            }
        }

        var observedRemote = parsed.ObservedRemoteOverride ?? CaptureObservedRemote(repoRoot);
        var targetRepo = parsed.TargetRepoOverride ?? bindingTargetRepo;

        var input = new IntentHostCheckInput
        {
            RepoRoot = repoRoot,
            Domain = parsed.Domain,
            TargetRepo = targetRepo,
            ObservedRemoteUrl = observedRemote,
            ChildSubmodulePath = parsed.ChildSubmodulePath,
            ConfigTomlPresent = File.Exists(configPath),
            HostBindingPresent = bindingPresent,
            BoundHostRepo = boundHostRepo,
            IntentsDomainDirectoryPresent = Directory.Exists(domainDir),
            AgentsMarkdownPresent = File.Exists(agentsPath),
            ClaudeMarkdownPresent = File.Exists(claudePath),
            QueueStateJsonPresent = File.Exists(queueStatePath),
            RunsJsonlPresent = File.Exists(runsLogPath)
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);
        var emitted = new IntentHostCheckEmittedResult
        {
            Domain = result.Domain,
            TargetRepo = result.TargetRepo,
            HostRepoRoot = result.HostRepoRoot,
            BoundHostRepo = result.BoundHostRepo,
            ObservedRemoteUrl = result.ObservedRemoteUrl,
            ChildSubmodulePath = result.ChildSubmodulePath,
            Classification = result.Classification,
            Ok = result.Ok,
            Summary = result.Summary,
            RemediationSteps = result.RemediationSteps
        };

        if (string.Equals(parsed.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(emitted, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, emitted);
        }
        return result.Ok ? 0 : 1;
    }

    internal static (string? HostRepo, string? TargetRepo) ParseHostBinding(string toml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toml);

        var raw = TomlSerializer.Deserialize<TomlTable>(toml);
        if (raw is not TomlTable model)
        {
            throw new InvalidOperationException("host-binding.toml did not deserialize to a TOML table.");
        }
        if (!model.TryGetValue("host", out var hostSection) || hostSection is not TomlTable hostTable)
        {
            return (null, null);
        }
        string? Get(string key) =>
            hostTable.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
                ? text
                : null;
        return (Get("host_repo"), Get("target_repo"));
    }

    private static string? CaptureObservedRemote(string repoRoot)
    {
        if (ObservedRemoteFactory is { } factory)
        {
            return factory(repoRoot);
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.ArgumentList.Add("remote");
            process.StartInfo.ArgumentList.Add("get-url");
            process.StartInfo.ArgumentList.Add("origin");
            process.StartInfo.WorkingDirectory = repoRoot;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout) ? stdout : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void WriteMarkdown(TextWriter writer, IntentHostCheckEmittedResult result)
    {
        writer.WriteLine($"# intent host-check (G309) — domain `{result.Domain}`");
        writer.WriteLine();
        writer.WriteLine($"- classification: **{result.Classification}**");
        writer.WriteLine($"- ok: {(result.Ok ? "yes" : "no")}");
        writer.WriteLine($"- host_repo_root: `{result.HostRepoRoot}`");
        if (!string.IsNullOrEmpty(result.BoundHostRepo))
        {
            writer.WriteLine($"- bound_host_repo: `{result.BoundHostRepo}`");
        }
        if (!string.IsNullOrEmpty(result.TargetRepo))
        {
            writer.WriteLine($"- target_repo: `{result.TargetRepo}`");
        }
        if (!string.IsNullOrEmpty(result.ObservedRemoteUrl))
        {
            writer.WriteLine($"- observed_remote_url: `{result.ObservedRemoteUrl}`");
        }
        if (!string.IsNullOrEmpty(result.ChildSubmodulePath))
        {
            writer.WriteLine($"- child_submodule_path: `{result.ChildSubmodulePath}`");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.RemediationSteps.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Remediation");
            foreach (var step in result.RemediationSteps)
            {
                writer.WriteLine($"- {step}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, CliContext context, out ParsedArgs parsed, out string error)
    {
        parsed = default!;
        error = string.Empty;

        string? domain = null;
        string? targetRepo = null;
        string? observedRemote = null;
        string? childSubmodulePath = null;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value."; return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value (owner/repo)."; return false;
                    }
                    targetRepo = args[++index].Trim();
                    break;
                case "--observed-remote":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--observed-remote requires a value."; return false;
                    }
                    observedRemote = args[++index].Trim();
                    break;
                case "--child-submodule-path":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--child-submodule-path requires a value."; return false;
                    }
                    childSubmodulePath = args[++index].Trim();
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json)."; return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}')."; return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'."; return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            // Fall back to the host config domain when --domain is omitted.
            domain = string.IsNullOrWhiteSpace(context.Config.Project.Domain)
                ? null
                : context.Config.Project.Domain;
        }
        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "intent host-check requires '--domain <name>'.";
            return false;
        }

        parsed = new ParsedArgs
        {
            Domain = domain!,
            TargetRepoOverride = targetRepo,
            ObservedRemoteOverride = observedRemote,
            ChildSubmodulePath = childSubmodulePath,
            Format = format
        };
        return true;
    }

    private sealed record ParsedArgs
    {
        public required string Domain { get; init; }
        public string? TargetRepoOverride { get; init; }
        public string? ObservedRemoteOverride { get; init; }
        public string? ChildSubmodulePath { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record IntentHostCheckEmittedResult
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("target_repo")] public required string? TargetRepo { get; init; }
    [JsonPropertyName("host_repo_root")] public required string HostRepoRoot { get; init; }
    [JsonPropertyName("bound_host_repo")] public required string? BoundHostRepo { get; init; }
    [JsonPropertyName("observed_remote_url")] public required string? ObservedRemoteUrl { get; init; }
    [JsonPropertyName("child_submodule_path")] public required string? ChildSubmodulePath { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("ok")] public required bool Ok { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("remediation_steps")] public required IReadOnlyList<string> RemediationSteps { get; init; }
}
