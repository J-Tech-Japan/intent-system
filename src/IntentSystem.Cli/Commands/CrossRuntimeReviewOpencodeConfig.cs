using System.Text;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>G842: renders the pinned OpenCode reviewer and builder config text.</summary>
internal static class CrossRuntimeReviewOpencodeConfig
{
    internal const string ReviewerConfigJson = "{\n  \"$schema\": \"https://opencode.ai/config.json\",\n  \"permission\": {\n    \"*\": \"deny\",\n    \"edit\": \"deny\",\n    \"bash\": \"deny\",\n    \"task\": \"deny\",\n    \"external_directory\": \"deny\",\n    \"webfetch\": \"deny\",\n    \"websearch\": \"deny\",\n    \"skill\": \"deny\",\n    \"todowrite\": \"deny\",\n    \"question\": \"deny\",\n    \"lsp\": \"deny\",\n    \"doom_loop\": \"deny\",\n    \"read\": \"allow\",\n    \"glob\": \"allow\",\n    \"grep\": \"allow\",\n    \"list\": \"allow\"\n  },\n  \"agent\": {\n    \"intent-cli-reviewer\": {\n      \"description\": \"intent-cli read-only cross-runtime reviewer\",\n      \"mode\": \"primary\",\n      \"permission\": {\n        \"*\": \"deny\",\n        \"edit\": \"deny\",\n        \"bash\": \"deny\",\n        \"task\": \"deny\",\n        \"external_directory\": \"deny\",\n        \"webfetch\": \"deny\",\n        \"websearch\": \"deny\",\n        \"skill\": \"deny\",\n        \"todowrite\": \"deny\",\n        \"question\": \"deny\",\n        \"lsp\": \"deny\",\n        \"doom_loop\": \"deny\",\n        \"read\": \"allow\",\n        \"glob\": \"allow\",\n        \"grep\": \"allow\",\n        \"list\": \"allow\"\n      }\n    }\n  }\n}\n";

    internal const string BuilderConfigJson =
        """
        {
          "$schema": "https://opencode.ai/config.json",
          "permission": {
            "external_directory": "deny",
            "bash": {
              "*": "allow",
              "git push*": "deny",
              "gh *": "deny"
            }
          }
        }
        """ + "\n";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static byte[] ReviewerConfigBytes(string? providerConfigPath = null)
    {
        if (string.IsNullOrEmpty(providerConfigPath))
        {
            return Utf8NoBom.GetBytes(ReviewerConfigJson);
        }

        if (!TryValidateProviderConfigFile(providerConfigPath, out var provider, out _))
        {
            throw new InvalidOperationException("invalid provider config.");
        }

        return RenderReviewerConfigWithProvider(provider);
    }

    public static bool TryValidateProviderConfigFile(string path, out JsonElement provider, out string error)
    {
        provider = default;
        byte[] bytes;
        try
        {
            CrossRuntimeReviewHomeAccessGuard.BeforeProtectedPathAccess(path);
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"file '{path}' could not be read: {exception.Message}";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            error = $"file '{path}' is not valid JSON: {exception.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = $"file '{path}' root must be a JSON object.";
                return false;
            }

            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 1 || !string.Equals(properties[0].Name, "provider", StringComparison.Ordinal))
            {
                error = $"file '{path}' must have exactly one top-level key 'provider'.";
                return false;
            }

            if (properties[0].Value.ValueKind != JsonValueKind.Object)
            {
                error = $"file '{path}' key 'provider' must be a JSON object.";
                return false;
            }

            provider = properties[0].Value.Clone();
            error = string.Empty;
            return true;
        }
    }

    internal static byte[] RenderReviewerConfigWithProvider(JsonElement provider)
    {
        using var measuredDocument = JsonDocument.Parse(ReviewerConfigJson);
        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append("  \"$schema\": \"https://opencode.ai/config.json\",\n");
        builder.Append("  \"provider\": ");
        AppendIndentedJsonElement(builder, provider, baseIndent: 2);

        foreach (var property in measuredDocument.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "$schema", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(",\n  \"");
            builder.Append(property.Name);
            builder.Append("\": ");
            AppendIndentedJsonElement(builder, property.Value, baseIndent: 2);
        }

        builder.Append("\n}\n");
        return Utf8NoBom.GetBytes(builder.ToString());
    }

    private static void AppendIndentedJsonElement(StringBuilder builder, JsonElement element, int baseIndent)
    {
        var lines = JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true }).Split('\n');
        builder.Append(lines[0]);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            var leading = line.Length - line.TrimStart().Length;
            builder.Append('\n');
            builder.Append(new string(' ', baseIndent + leading));
            builder.Append(line.TrimStart());
        }
    }

    private static void AppendJsonElement(StringBuilder builder, JsonElement element, int indent)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var lines = JsonSerializer.Serialize(element, options).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            builder.Append(new string(' ', indent));
            builder.Append(lines[index].TrimStart());
        }
    }
}
