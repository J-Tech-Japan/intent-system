using System.Text.Json;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Commands;

/// <summary>Read-only packet source validation, usable without host config.</summary>
internal static class PacketValidateSourcesCommand
{
    private const string Usage = "Usage: intent-cli packet validate-sources --execution-unit <unit> --format json|markdown";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Execute(CliContext context, string[] args, TextWriter writer) =>
        Execute(context.RepoRoot, args, writer, DateTimeOffset.UtcNow, null);

    internal static int Execute(string repoRoot, string[] args, TextWriter writer) =>
        Execute(repoRoot, args, writer, DateTimeOffset.UtcNow, null);

    internal static int Execute(string repoRoot, string[] args, TextWriter writer, DateTimeOffset now,
        Action<string, string>? beforeRulingRead)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);
        if (args.Length == 1 && args[0] == "--help")
        {
            writer.WriteLine(Usage);
            writer.WriteLine("Validates declared ruling pins and the authored public provenance block. It does not edit files or verify publication.");
            return 0;
        }

        string? unit = null;
        string? format = null;
        var supportedFormat = FindSupportedFormat(args);
        var parseError = string.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length) { parseError = $"Missing value for '{args[i]}'."; break; }
            var value = args[++i];
            switch (args[i - 1])
            {
                case "--execution-unit" when unit is null: unit = value; break;
                case "--format" when format is null: format = value; break;
                default: parseError = $"Unknown or duplicate argument '{args[i - 1]}'."; break;
            }
            if (parseError.Length > 0) break;
        }

        var outputFormat = format is "json" or "markdown" ? format : supportedFormat;
        if (outputFormat is not ("json" or "markdown"))
        {
            writer.WriteLine(parseError.Length == 0 ? "Format must be json or markdown." : parseError);
            writer.WriteLine(Usage);
            return 1;
        }

        if (unit is null || parseError.Length > 0)
        {
            var invalid = new PacketScopeSources.Result(unit ?? string.Empty, "refused", "scope-sources-invalid-declaration",
                parseError.Length == 0 ? "--execution-unit is required" : parseError,
                RulingArtifact.FormatTimestamp(now), null, null, []);
            Emit(writer, outputFormat, invalid);
            return 1;
        }

        if (!RulingArtifact.TryIdentifier(unit, out var unitError))
        {
            var invalid = new PacketScopeSources.Result(unit, "refused", "scope-sources-identity-mismatch", unitError,
                RulingArtifact.FormatTimestamp(now), null, null, []);
            Emit(writer, outputFormat, invalid);
            return 1;
        }

        var packetDirectory = Path.Combine(repoRoot, ".intent-cli", "issues", unit);
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(Path.Combine(packetDirectory, "packet.yaml"),
                out var packetYaml, out var packetFailure, out var packetError))
        {
            var unavailable = PacketFailure(unit, now, "packet.yaml", packetFailure, packetError);
            Emit(writer, outputFormat, unavailable);
            return 1;
        }
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(Path.Combine(packetDirectory, "github-body.md"),
                out var body, out var bodyFailure, out var bodyError))
        {
            var unavailable = PacketFailure(unit, now, "github-body.md", bodyFailure, bodyError);
            Emit(writer, outputFormat, unavailable);
            return 1;
        }

        var result = PacketScopeSources.Evaluate(repoRoot, unit, packetYaml, body, now, beforeRulingRead);
        Emit(writer, outputFormat, result);
        return result.IsSuccessful ? 0 : 1;
    }

    private static string? FindSupportedFormat(string[] args)
    {
        for (var index = 0; index + 1 < args.Length; index++)
            if (args[index] == "--format" && args[index + 1] is "json" or "markdown") return args[index + 1];
        return null;
    }

    private static PacketScopeSources.Result PacketFailure(string unit, DateTimeOffset now, string file,
        CrossRuntimeReviewFileReadFailure failure, string error) =>
        new(unit, "unavailable", "scope-sources-packet-unavailable", $"{file}: {failure}: {error}",
            RulingArtifact.FormatTimestamp(now), null, null,
            [new("packet-unavailable", $".intent-cli/issues/{unit}/{file}", $"{failure}: {error}")]);

    internal static void Emit(TextWriter writer, string format, PacketScopeSources.Result result)
    {
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine($"# Packet ruling source validation: {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- State: {result.State}");
        writer.WriteLine($"- Cause: {result.Cause}");
        writer.WriteLine($"- Detail: {result.Detail}");
        writer.WriteLine($"- Evaluated at: {result.EvaluatedAt}");
        writer.WriteLine("- Authority verification: supplied-not-authenticated");
        writer.WriteLine("- Publication: not-verified");
        if (result.Provenance is { Count: > 0 })
        {
            writer.WriteLine();
            writer.WriteLine("## Verified sources");
            foreach (var source in result.Provenance)
                writer.WriteLine($"- {source.Reference} | {source.Domain}/{source.Team} | {source.TargetRepo} | {source.ExecutionUnit} | {source.Sha256} | `{source.Path}`");
        }
        if (result.ExpectedProvenanceBlock is not null)
        {
            writer.WriteLine();
            writer.WriteLine("## Expected public block");
            writer.WriteLine("```text");
            writer.WriteLine(result.ExpectedProvenanceBlock);
            writer.WriteLine("```");
        }
        if (result.Diagnostics.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Diagnostics");
            foreach (var diagnostic in result.Diagnostics)
                writer.WriteLine($"- {diagnostic.Cause}{(diagnostic.Path is null ? "" : $" (`{diagnostic.Path}`)")}: {diagnostic.Detail}");
        }
    }
}
