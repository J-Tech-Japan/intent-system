using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class OrcaRunSoloStore
{
    public const string SchemaVersion = "1";
    public const string RelativeIgnorePath = ".intent-cli/orca-runs/.gitignore";
    public const string RelativeRoot = ".intent-cli/orca-runs";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string RelativePathFor(string domain, string team) =>
        $"{RelativeRoot}/{domain}/{team}.json";

    public static string ResolvePath(string routingRoot, string domain, string team) =>
        Path.GetFullPath(Path.Combine(routingRoot, RelativePathFor(domain, team).Replace('/', Path.DirectorySeparatorChar)));

    public static string ResolveIgnorePath(string routingRoot) =>
        Path.GetFullPath(Path.Combine(routingRoot, RelativeIgnorePath.Replace('/', Path.DirectorySeparatorChar)));

    public static void EnsureLocalIgnore(string routingRoot)
    {
        var path = ResolveIgnorePath(routingRoot);
        var content = "*" + Environment.NewLine;
        if (File.Exists(path) && string.Equals(GuardedFileRead.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        WriteAtomically(path, content);
    }

    public static bool Exists(string routingRoot, string domain, string team) =>
        File.Exists(ResolvePath(routingRoot, domain, team));

    public static SoloBindingReadResult TryRead(string routingRoot, string domain, string team)
    {
        var path = ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            return SoloBindingReadResult.Missing(path);
        }

        try
        {
            var text = GuardedFileRead.ReadAllText(path);
            var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var fileDomain = root.TryGetProperty("domain", out var domainElement) && domainElement.ValueKind == JsonValueKind.String
                ? domainElement.GetString()
                : null;
            var fileTeam = root.TryGetProperty("team", out var teamElement) && teamElement.ValueKind == JsonValueKind.String
                ? teamElement.GetString()
                : null;
            string? schemaVersion = root.TryGetProperty("schema_version", out var schemaElement) && schemaElement.ValueKind == JsonValueKind.String
                ? schemaElement.GetString()
                : null;
            string? role = null;
            string? runId = null;
            string? receivePolicy = null;
            string? frontend = null;
            if (root.TryGetProperty("orca_run", out var orcaRun) && orcaRun.ValueKind == JsonValueKind.Object)
            {
                role = orcaRun.TryGetProperty("role", out var roleElement) && roleElement.ValueKind == JsonValueKind.String
                    ? roleElement.GetString()
                    : null;
                runId = orcaRun.TryGetProperty("run_id", out var runIdElement) && runIdElement.ValueKind == JsonValueKind.String
                    ? runIdElement.GetString()
                    : null;
                receivePolicy = orcaRun.TryGetProperty("receive_policy", out var policyElement) && policyElement.ValueKind == JsonValueKind.String
                    ? policyElement.GetString()
                    : null;
                frontend = orcaRun.TryGetProperty("frontend", out var frontendElement) && frontendElement.ValueKind == JsonValueKind.String
                    ? frontendElement.GetString()
                    : null;
            }

            return new SoloBindingReadResult
            {
                Exists = true,
                Path = path,
                Parsed = true,
                Domain = fileDomain,
                Team = fileTeam,
                SchemaVersion = schemaVersion,
                Role = role,
                RunId = runId,
                ReceivePolicy = receivePolicy,
                Frontend = frontend,
                HasOrcaRunObject = root.TryGetProperty("orca_run", out var node) && node.ValueKind == JsonValueKind.Object,
                OrcaRunNull = root.TryGetProperty("orca_run", out var orcaNode) && orcaNode.ValueKind == JsonValueKind.Null,
                OrcaRunAbsent = !root.TryGetProperty("orca_run", out _),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return SoloBindingReadResult.Unparseable(path, exception.Message);
        }
    }

    public static string Serialize(string domain, string team, string role, string runId, string receivePolicy, string frontend) =>
        JsonSerializer.Serialize(new
        {
            schema_version = SchemaVersion,
            domain,
            team,
            orca_run = new
            {
                role,
                run_id = runId,
                receive_policy = receivePolicy,
                frontend,
            },
        }, Options);

    public static void Write(string routingRoot, string domain, string team, string role, string runId, string receivePolicy, string frontend)
    {
        EnsureLocalIgnore(routingRoot);
        var path = ResolvePath(routingRoot, domain, team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomically(path, Serialize(domain, team, role, runId, receivePolicy, frontend));
    }

    public static void Delete(string routingRoot, string domain, string team)
    {
        var path = ResolvePath(routingRoot, domain, team);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string ComputeDigest(string routingRoot, string domain, string team)
    {
        var path = ResolvePath(routingRoot, domain, team);
        if (!File.Exists(path))
        {
            return "absent";
        }

        return OrcaRunTeamModeLock.ComputeDigest(GuardedFileRead.ReadAllBytes(path));
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

internal sealed record SoloBindingReadResult
{
    public bool Exists { get; init; }
    public required string Path { get; init; }
    public bool Parsed { get; init; }
    public string? Domain { get; init; }
    public string? Team { get; init; }
    public string? SchemaVersion { get; init; }
    public string? Role { get; init; }
    public string? RunId { get; init; }
    public string? ReceivePolicy { get; init; }
    public string? Frontend { get; init; }
    public bool HasOrcaRunObject { get; init; }
    public bool OrcaRunNull { get; init; }
    public bool OrcaRunAbsent { get; init; }
    public bool IsUnparseable { get; init; }
    public string? UnparseableMessage { get; init; }

    public static SoloBindingReadResult Missing(string path) => new()
    {
        Exists = false,
        Path = path,
    };

    public static SoloBindingReadResult Unparseable(string path, string message) => new()
    {
        Exists = true,
        Path = path,
        IsUnparseable = true,
        UnparseableMessage = message,
    };
}
