using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G326: read-only <c>intent-cli guide host-ownership</c>. Emits the
/// canonical role-scoped host topology — design / review-runtime /
/// child-worker / ambiguous — with each role's responsibilities and
/// the may-write / must-not-write matrix that <see cref="HostOwnershipModel"/>
/// defines. Operators and downstream tooling (closeout, packet draft,
/// publish-flow) reference this to decide what the current cwd is
/// allowed to mutate. Never mutates state. Never launches a provider.
/// </summary>
internal static class GuideHostOwnershipCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide host-ownership [--role design|review-runtime|child-worker|ambiguous] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var rawRole, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        string? focusRoleId = null;
        if (!string.IsNullOrWhiteSpace(rawRole))
        {
            focusRoleId = HostOwnershipModel.ResolveRole(rawRole);
            if (string.Equals(focusRoleId, HostOwnershipModel.RoleAmbiguous, StringComparison.Ordinal)
                && !string.Equals(rawRole, HostOwnershipModel.RoleAmbiguous, StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteLine(
                    $"--role '{rawRole}' did not resolve to a known role (design / review-runtime / child-worker / ambiguous).");
                writer.WriteLine(UsageLine);
                return 1;
            }
        }

        var result = new GuideHostOwnershipResult
        {
            FocusRole = focusRoleId,
            Roles = HostOwnershipModel.Roles
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    private static void WriteMarkdown(TextWriter writer, GuideHostOwnershipResult result)
    {
        writer.WriteLine("# Guide host-ownership (G326)");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.FocusRole))
        {
            writer.WriteLine($"- focus role: `{result.FocusRole}`");
            writer.WriteLine();
        }

        var roles = string.IsNullOrWhiteSpace(result.FocusRole)
            ? result.Roles
            : result.Roles.Where(r => string.Equals(r.Id, result.FocusRole, StringComparison.Ordinal)).ToArray();

        foreach (var role in roles)
        {
            writer.WriteLine($"## `{role.Id}`");
            writer.WriteLine();
            writer.WriteLine(role.Summary);
            writer.WriteLine();
            writer.WriteLine("### Responsibilities");
            foreach (var item in role.Responsibilities)
            {
                writer.WriteLine($"- {item}");
            }
            writer.WriteLine();
            writer.WriteLine("### May write");
            if (role.MayWrite.Count == 0)
            {
                writer.WriteLine("- _(nothing — ambiguous role refuses durable writes until a deterministic role binding is supplied)_");
            }
            else
            {
                foreach (var item in role.MayWrite)
                {
                    writer.WriteLine($"- {item}");
                }
            }
            writer.WriteLine();
            writer.WriteLine("### Must NOT write");
            foreach (var item in role.MustNotWrite)
            {
                writer.WriteLine($"- {item}");
            }
            writer.WriteLine();
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? role,
        out string format,
        out string error)
    {
        role = null;
        format = FormatJson; // controller-friendly default
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--role":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--role requires a value (design, review-runtime, child-worker, or ambiguous).";
                        return false;
                    }
                    role = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }
        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide host-ownership (G326)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Emit the role-scoped host topology: design / review-runtime / child-worker / ambiguous,");
        writer.WriteLine("with each role's responsibilities and the may-write / must-not-write matrix.");
        writer.WriteLine("Default JSON output is controller-friendly; markdown is the operator-facing rendering.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideHostOwnershipResult
{
    /// <summary>
    /// G326: optional focus role (when the caller passed
    /// <c>--role &lt;x&gt;</c>). Null indicates the caller asked for the
    /// full matrix.
    /// </summary>
    [JsonPropertyName("focus_role")]
    public string? FocusRole { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<HostOwnershipRole> Roles { get; init; }
}
