using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G686: an operator-recorded comparator baseline. This is deliberately a
/// separate type from <see cref="AgentLaunchRecipe"/>: it is never a launch
/// recipe and is never consumed by recovery or seat start.
/// </summary>
internal sealed record AgentLaunchEnvelopeProfile
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("sandbox_mode")]
    public required string SandboxMode { get; init; }

    [JsonPropertyName("approval_mode")]
    public required string ApprovalMode { get; init; }

    [JsonPropertyName("roots_policy")]
    public required string RootsPolicy { get; init; }

    [JsonPropertyName("writable_roots")]
    public required IReadOnlyList<string> WritableRoots { get; init; }

    [JsonPropertyName("network_access")]
    public required string NetworkAccess { get; init; }

    [JsonPropertyName("transport_mode")]
    public required string TransportMode { get; init; }

    [JsonPropertyName("evidence")]
    public required string Evidence { get; init; }

    [JsonPropertyName("recorded_at")]
    public required string RecordedAt { get; init; }

    [JsonPropertyName("permission_options")]
    public IReadOnlyList<string> PermissionOptions { get; init; } = [];

    [JsonPropertyName("network_urls")]
    public IReadOnlyList<string> NetworkUrls { get; init; } = [];

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }
}

internal static class AgentLaunchEnvelopeProfileCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ComputeDigest(AgentLaunchEnvelopeProfile profile)
    {
        var canonical = string.Join('\u001f',
            profile.Name,
            profile.Kind,
            profile.SandboxMode,
            profile.ApprovalMode,
            profile.RootsPolicy,
            string.Join('\u001e', profile.WritableRoots.OrderBy(value => value, StringComparer.Ordinal)),
            profile.NetworkAccess,
            profile.TransportMode,
            profile.Evidence,
            profile.RecordedAt,
            string.Join('\u001e', profile.PermissionOptions.OrderBy(value => value, StringComparer.Ordinal)),
            string.Join('\u001e', profile.NetworkUrls.OrderBy(value => value, StringComparer.Ordinal)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static AgentLaunchEnvelopeProfile WithDigest(AgentLaunchEnvelopeProfile profile) =>
        profile with { Digest = ComputeDigest(profile) };

    public static JsonObject ToJsonObject(AgentLaunchEnvelopeProfile profile) =>
        JsonNode.Parse(JsonSerializer.Serialize(profile, JsonOptions))!.AsObject();

    public static bool TryRead(
        JsonElement element,
        string name,
        out AgentLaunchEnvelopeProfile? profile,
        out string error)
    {
        profile = null;
        error = string.Empty;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"profile '{name}' is not an object.";
            return false;
        }

        var kind = ReadString(element, "kind");
        var sandboxMode = ReadString(element, "sandbox_mode");
        var approvalMode = ReadString(element, "approval_mode");
        var rootsPolicy = ReadString(element, "roots_policy");
        var networkAccess = ReadString(element, "network_access");
        var transportMode = ReadString(element, "transport_mode");
        var evidence = ReadString(element, "evidence");
        var recordedAt = ReadString(element, "recorded_at");
        if (new[] { kind, sandboxMode, approvalMode, rootsPolicy, networkAccess, transportMode, evidence, recordedAt }
            .Any(string.IsNullOrWhiteSpace))
        {
            error = $"profile '{name}' must contain non-empty kind, sandbox_mode, approval_mode, roots_policy, "
                + "network_access, transport_mode, evidence, and recorded_at fields.";
            return false;
        }

        if (!DateTimeOffset.TryParse(recordedAt, out _))
        {
            error = $"profile '{name}' has an invalid recorded_at value '{recordedAt}'.";
            return false;
        }

        if (!TryReadStringArray(element, "writable_roots", out var writableRoots, out error)
            || !TryReadStringArray(element, "permission_options", out var permissionOptions, out error)
            || !TryReadStringArray(element, "network_urls", out var networkUrls, out error))
        {
            error = $"profile '{name}' {error}";
            return false;
        }

        profile = new AgentLaunchEnvelopeProfile
        {
            Name = name,
            Kind = kind!,
            SandboxMode = sandboxMode!,
            ApprovalMode = approvalMode!,
            RootsPolicy = rootsPolicy!,
            WritableRoots = writableRoots,
            NetworkAccess = networkAccess!,
            TransportMode = transportMode!,
            Evidence = evidence!,
            RecordedAt = recordedAt!,
            PermissionOptions = permissionOptions,
            NetworkUrls = networkUrls,
            Digest = ReadString(element, "digest"),
        };

        var expectedDigest = ComputeDigest(profile);
        if (profile.Digest is not null
            && !string.Equals(profile.Digest, expectedDigest, StringComparison.OrdinalIgnoreCase))
        {
            error = $"profile '{name}' digest '{profile.Digest}' does not match its recorded fields (expected '{expectedDigest}').";
            profile = null;
            return false;
        }

        profile = profile with { Digest = profile.Digest ?? expectedDigest };
        return true;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadStringArray(
        JsonElement element,
        string property,
        out IReadOnlyList<string> values,
        out string error)
    {
        values = [];
        error = string.Empty;
        if (!element.TryGetProperty(property, out var value))
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = $"field '{property}' must be an array.";
            return false;
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                error = $"field '{property}' must contain only non-empty strings.";
                return false;
            }
            result.Add(item.GetString()!);
        }

        values = result;
        return true;
    }
}
