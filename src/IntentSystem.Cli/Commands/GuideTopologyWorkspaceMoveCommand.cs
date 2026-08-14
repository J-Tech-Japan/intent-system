using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G697: installed, read-only operator recipe for the topology workspace
/// move. Rendering this guide never reads or writes machine-local topology.
/// </summary>
internal static class GuideTopologyWorkspaceMoveCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";
    private const string UsageLine =
        "Usage: intent-cli guide topology-workspace-move [--domain <name>] [--team <name>] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            writer.WriteLine("Render the dry-run-first, CAS-guarded topology workspace move recipe and verification commands.");
            return 0;
        }

        if (!TryParseArguments(args, out var domain, out var team, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var guide = TopologyWorkspaceMoveGuidance.Create(domain, team);
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(guide, JsonOptions));
            writer.WriteLine();
            return 0;
        }

        writer.WriteLine("# Guide — topology workspace move (G697)");
        writer.WriteLine();
        writer.WriteLine("This is a read-only recipe. Inspect and preview first; apply only after the operator confirms the full before/after state.");
        writer.WriteLine();
        writer.WriteLine("## Canonical workflow");
        writer.WriteLine($"1. inspect: `{guide.Commands.Inspect}`");
        writer.WriteLine($"2. preview: `{guide.Commands.Preview}`");
        writer.WriteLine($"3. apply: `{guide.Commands.Apply}`");
        writer.WriteLine($"4. validate: `{guide.Commands.Validate}`");
        writer.WriteLine($"5. notify preflight: `{guide.Commands.NotifyPreflight}`");
        writer.WriteLine();
        writer.WriteLine("## Contracts");
        writer.WriteLine($"- pane mapping: {guide.PaneMapContract}");
        writer.WriteLine($"- preservation: {guide.PreservationContract}");
        writer.WriteLine($"- CAS: {guide.CasContract}");
        writer.WriteLine($"- authority boundary: {guide.AuthorityBoundary}");
        writer.WriteLine();
        writer.WriteLine("## Reachability");
        foreach (var route in guide.Routes)
        {
            writer.WriteLine($"- role `{route.Role}` reaches `{route.GuideSurface}` for {route.TargetSurface}.");
        }

        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? team,
        out string format,
        out string error)
    {
        domain = null;
        team = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (!TryReadValue(args, ref index, "--domain", out domain, out error)) return false;
                    break;
                case "--team":
                    if (!TryReadValue(args, ref index, "--team", out team, out error)) return false;
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, "--format", out var requestedFormat, out error)) return false;
                    if (requestedFormat is not (FormatMarkdown or FormatJson))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat!;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index].Trim();
        error = string.Empty;
        return true;
    }
}
