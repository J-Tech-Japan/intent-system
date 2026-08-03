using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G601: writes the deliberately small, team-scoped session-layer display block
/// in an operator-owned agent startup file. The canonical mode record remains
/// authoritative; this block is a generated, verifiable signpost to it.
/// </summary>
internal static class SessionLayerMarkerCommand
{
    private const string Usage =
        "Usage: intent-cli session-layer marker generate --domain <name> --team <name> --file <AGENTS.md|CLAUDE.md> [--dry-run|--write] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "--help"))
        {
            writer.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        if (!string.Equals(args[0], "generate", StringComparison.Ordinal))
        {
            writer.WriteLine($"Unknown marker subcommand '{args[0]}'.");
            writer.WriteLine(Usage);
            return 1;
        }

        if (!TryParse(args[1..], out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        if (!TryResolveStartupFile(context.RepoRoot, request!.File, out var path, out var relativePath, out error))
        {
            return Emit(writer, request.Format, new SessionLayerMarkerGenerateResult
            {
                Written = false,
                File = request.File,
                Cause = "invalid-startup-file",
                Summary = error,
            });
        }

        SessionLayerModeResolution resolution;
        try
        {
            resolution = SessionLayerModeStore.Resolve(context.RepoRoot, request.Domain, request.Team);
        }
        catch (InvalidOperationException exception)
        {
            return Emit(writer, request.Format, new SessionLayerMarkerGenerateResult
            {
                Written = false,
                File = relativePath,
                Cause = "session-layer-mode-unreadable",
                Summary = exception.Message,
            });
        }

        if (resolution.Source != SessionLayerModeSource.Recorded || resolution.Entry is null)
        {
            var recordingCommand = RecordingCommand(request.Domain, request.Team);
            return Emit(writer, request.Format, new SessionLayerMarkerGenerateResult
            {
                Written = false,
                File = relativePath,
                Cause = "session-layer-mode-unrecorded",
                RecordingCommand = recordingCommand,
                Summary = $"Refusing to generate a marker for unrecorded team '{request.Team}' in domain "
                    + $"'{request.Domain}'. Record its mode first with `{recordingCommand}`.",
            });
        }

        var content = File.ReadAllText(path);
        var parsed = SessionLayerMarkerStore.Parse(relativePath, content);
        if (parsed.Error is not null)
        {
            return Emit(writer, request.Format, new SessionLayerMarkerGenerateResult
            {
                Written = false,
                File = relativePath,
                Cause = "marker-malformed",
                Summary = parsed.Error,
            });
        }

        var candidates = parsed.Blocks
            .Where(block => string.Equals(block.Domain, request.Domain, StringComparison.Ordinal)
                && string.Equals(block.Team, request.Team, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length != 1)
        {
            return Emit(writer, request.Format, new SessionLayerMarkerGenerateResult
            {
                Written = false,
                File = relativePath,
                Cause = candidates.Length == 0 ? "marker-absent" : "marker-malformed",
                Summary = candidates.Length == 0
                    ? $"Refusing to touch '{relativePath}' because it has no explicit managed marker block for "
                        + $"domain '{request.Domain}' and team '{request.Team}'. Add the delimited placeholder from the guide first."
                    : $"Refusing to touch '{relativePath}' because it has {candidates.Length} managed marker blocks for "
                        + $"domain '{request.Domain}' and team '{request.Team}'.",
            });
        }

        var marker = candidates[0];
        if (!marker.IsEmpty && !marker.IsGenerated)
        {
            return Emit(writer, request.Format, new SessionLayerMarkerGenerateResult
            {
                Written = false,
                File = relativePath,
                Cause = "marker-malformed",
                Summary = $"Refusing to touch malformed managed marker block in '{relativePath}' for domain "
                    + $"'{request.Domain}' and team '{request.Team}'.",
            });
        }

        var recordHash = SessionLayerMarkerStore.Hash(resolution.Entry);
        var replacement = SessionLayerMarkerStore.Render(request.Domain, request.Team, resolution.Mode, recordHash);
        var updated = content[..marker.StartIndex] + replacement + content[marker.EndIndex..];
        if (request.Write && !string.Equals(content, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(path, updated);
        }

        return Emit(writer, request.Format, new SessionLayerMarkerGenerateResult
        {
            Written = request.Write && !string.Equals(content, updated, StringComparison.Ordinal),
            File = relativePath,
            Domain = request.Domain,
            Team = request.Team,
            Mode = resolution.Mode,
            VerifyCommand = SessionLayerMarkerStore.VerifyCommand(request.Domain, request.Team),
            RecordHash = recordHash,
            Summary = request.Write
                ? $"Generated the managed session-layer marker for team '{request.Team}' in '{relativePath}'."
                : $"Would generate the managed session-layer marker for team '{request.Team}' in '{relativePath}'.",
        });
    }

    private static int Emit(TextWriter writer, string format, SessionLayerMarkerGenerateResult result)
    {
        if (string.Equals(format, "json", StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine(result.Summary);
            if (result.VerifyCommand is not null) writer.WriteLine($"Verify: `{result.VerifyCommand}`");
            if (result.RecordingCommand is not null) writer.WriteLine($"Record first: `{result.RecordingCommand}`");
        }

        return result.Cause is null ? 0 : 1;
    }

    private static bool TryParse(string[] args, out SessionLayerMarkerGenerateRequest? request, out string error)
    {
        request = null;
        error = string.Empty;
        string? domain = null;
        string? team = null;
        string? file = null;
        var write = false;
        var format = "markdown";
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain": if (!ReadValue(args, ref index, "--domain", out domain, out error)) return false; break;
                case "--team": if (!ReadValue(args, ref index, "--team", out team, out error)) return false; break;
                case "--file": if (!ReadValue(args, ref index, "--file", out file, out error)) return false; break;
                case "--write": write = true; break;
                case "--dry-run": write = false; break;
                case "--format":
                    if (!ReadValue(args, ref index, "--format", out format, out error)) return false;
                    if (format is not ("markdown" or "json")) { error = "--format must be markdown or json."; return false; }
                    break;
                default: error = $"Unknown argument '{args[index]}'."; return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(file))
        {
            error = "--domain, --team, and --file are required.";
            return false;
        }

        request = new SessionLayerMarkerGenerateRequest(domain, team, file, write, format);
        return true;
    }

    private static bool ReadValue(string[] args, ref int index, string option, out string? value, out string error)
    {
        value = null;
        error = string.Empty;
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index].Trim();
        return true;
    }

    private static bool TryResolveStartupFile(string root, string requested, out string path, out string relative, out string error)
    {
        path = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested));
        relative = Path.GetRelativePath(root, path);
        error = string.Empty;
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(path), "AGENTS.md", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), "CLAUDE.md", StringComparison.OrdinalIgnoreCase))
        {
            error = "--file must name an existing AGENTS.md or CLAUDE.md inside the host repository.";
            return false;
        }

        if (!File.Exists(path))
        {
            error = $"Startup file '{relative}' does not exist; refusing to create it.";
            return false;
        }

        relative = relative.Replace(Path.DirectorySeparatorChar, '/');
        return true;
    }

    internal static string RecordingCommand(string domain, string team) =>
        $"intent-cli session-layer set --domain {domain} --team {team} --mode agmsg|herdr-only --write";

    private sealed record SessionLayerMarkerGenerateRequest(string Domain, string Team, string File, bool Write, string Format);
}

internal sealed record SessionLayerMarkerGenerateResult
{
    [JsonPropertyName("written")] public required bool Written { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("team")] public string? Team { get; init; }
    [JsonPropertyName("mode")] public string? Mode { get; init; }
    [JsonPropertyName("verify_command")] public string? VerifyCommand { get; init; }
    [JsonPropertyName("record_hash")] public string? RecordHash { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("recording_command")] public string? RecordingCommand { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal static class SessionLayerMarkerStore
{
    private const string Prefix = "intent-cli:session-layer-marker:";
    private static readonly Regex Token = new("<!--\\s*" + Prefix + "(?<kind>start|end)[^>]*-->", RegexOptions.Compiled);
    private static readonly Regex Start = new("^<!--\\s*" + Prefix + "start\\s+domain=\\\"(?<domain>[^\\\"]+)\\\"\\s+team=\\\"(?<team>[^\\\"]+)\\\"\\s*-->$", RegexOptions.Compiled);
    private static readonly Regex End = new("^<!--\\s*" + Prefix + "end\\s*-->$", RegexOptions.Compiled);
    private static readonly Regex Claim = new("^<!--\\s*" + Prefix + "claim\\s+domain=\\\"(?<domain>[^\\\"]+)\\\"\\s+team=\\\"(?<team>[^\\\"]+)\\\"\\s+mode=\\\"(?<mode>[^\\\"]+)\\\"\\s+verify-command=\\\"(?<verify>[^\\\"]+)\\\"\\s+record-hash=\\\"(?<hash>sha256:[0-9a-f]{64})\\\"\\s*-->$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions HashJsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string VerifyCommand(string domain, string team) =>
        $"intent-cli session-layer show --domain {domain} --team {team}";

    public static string Hash(SessionLayerModeEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, HashJsonOptions);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static string Render(string domain, string team, string mode, string recordHash) =>
        $"<!-- {Prefix}start domain=\"{domain}\" team=\"{team}\" -->\n"
        + $"<!-- {Prefix}claim domain=\"{domain}\" team=\"{team}\" mode=\"{mode}\" "
        + $"verify-command=\"{VerifyCommand(domain, team)}\" record-hash=\"{recordHash}\" -->\n"
        + $"<!-- {Prefix}end -->";

    public static SessionLayerMarkerParseResult Parse(string file, string content)
    {
        var blocks = new List<SessionLayerMarkerBlock>();
        SessionLayerMarkerBlock? open = null;
        foreach (Match token in Token.Matches(content))
        {
            var kind = token.Groups["kind"].Value;
            if (kind == "start")
            {
                var start = Start.Match(token.Value);
                if (!start.Success || open is not null)
                {
                    return new([], $"Managed marker syntax is malformed in '{file}'.");
                }

                open = new SessionLayerMarkerBlock
                {
                    File = file,
                    Domain = start.Groups["domain"].Value,
                    Team = start.Groups["team"].Value,
                    StartIndex = token.Index,
                };
            }
            else
            {
                if (!End.IsMatch(token.Value) || open is null)
                {
                    return new([], $"Managed marker syntax is malformed in '{file}'.");
                }

                var bodyStart = content.IndexOf('\n', open.StartIndex) + 1;
                if (bodyStart == 0) bodyStart = token.Index;
                var body = content[bodyStart..token.Index].Trim();
                var claim = Claim.Match(body);
                var generated = claim.Success
                    && string.Equals(claim.Groups["domain"].Value, open.Domain, StringComparison.Ordinal)
                    && string.Equals(claim.Groups["team"].Value, open.Team, StringComparison.Ordinal)
                    && string.Equals(claim.Groups["verify"].Value, VerifyCommand(open.Domain, open.Team), StringComparison.Ordinal);
                blocks.Add(open with
                {
                    EndIndex = token.Index + token.Length,
                    IsEmpty = body.Length == 0,
                    IsGenerated = generated,
                    Mode = generated ? claim.Groups["mode"].Value : null,
                    RecordHash = generated ? claim.Groups["hash"].Value : null,
                });
                open = null;
            }
        }

        return open is not null
            ? new([], $"Managed marker syntax is malformed in '{file}'.")
            : new(blocks, null);
    }

    public static IEnumerable<string> StartupFiles(string repoRoot)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj" };
        return Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => (string.Equals(Path.GetFileName(path), "AGENTS.md", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(path), "CLAUDE.md", StringComparison.OrdinalIgnoreCase))
                && !Path.GetRelativePath(repoRoot, path).Split(Path.DirectorySeparatorChar).Any(ignored.Contains))
            .OrderBy(path => path, StringComparer.Ordinal);
    }
}

internal sealed record SessionLayerMarkerBlock
{
    public required string File { get; init; }
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public bool IsEmpty { get; init; }
    public bool IsGenerated { get; init; }
    public string? Mode { get; init; }
    public string? RecordHash { get; init; }
}

internal sealed record SessionLayerMarkerParseResult(IReadOnlyList<SessionLayerMarkerBlock> Blocks, string? Error);
