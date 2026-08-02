using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G573: common guide-group output boundary for a best-effort, local-only
/// shipped-skill update nudge. The guide command runs first and its exit code
/// remains authoritative; probe or JSON-shaping failures leave its bytes alone.
/// </summary>
internal static class GuideSkillUpdateNudge
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Execute(
        CliContext context,
        string[] args,
        TextWriter writer,
        Func<TextWriter, int> executeGuide)
    {
        using var buffer = new StringWriter();
        var exitCode = executeGuide(buffer);
        var output = buffer.ToString();
        var location = SkillCommand.FindStaleShippedInstall(context);
        if (location is null)
        {
            writer.Write(output);
            return exitCode;
        }

        var command = $"intent-cli skill install --target {location.Target} --scope {location.Scope} --skill {location.Skill}";
        var nudge = $"Skill update available: run `{command}`.";

        if (RequestsJson(args))
        {
            try
            {
                if (JsonNode.Parse(output) is JsonObject root)
                {
                    root["skill_update_nudge"] = nudge;
                    writer.WriteLine(root.ToJsonString(JsonOptions));
                    return exitCode;
                }
            }
            catch (JsonException)
            {
                // Preserve the guide command's original error/output shape.
            }

            writer.Write(output);
            return exitCode;
        }

        writer.Write(output);
        if (output.Length > 0 && !output.EndsWith('\n'))
        {
            writer.WriteLine();
        }
        writer.WriteLine();
        writer.WriteLine($"> {nudge}");
        return exitCode;
    }

    private static bool RequestsJson(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--format", StringComparison.Ordinal)
                && string.Equals(args[index + 1], "json", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
