using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G196: <c>intent-cli tasking handoff-bundle-verify</c>. A LOCAL deterministic
/// preflight check command that consumes a G194
/// <see cref="TaskingHandoffBundleArtifact"/> handoff bundle artifact (JSON)
/// and prints a pass/fail verification result to STDOUT. The command performs
/// no GitHub network calls, applies no labels, launches no provider processes,
/// and does NOT touch <c>.intent-cli/queue-state.json</c> or
/// <c>.intent-cli/runs.jsonl</c>. It also does NOT write any artifact file —
/// verify is a read-only check that emits to STDOUT only.
///
/// Sits beside G190 <c>handoff</c>, G191 <c>task-packet</c>, G192
/// <c>task-packet-preview</c>, G193 <c>task-packet-checklist</c>, G194
/// <c>handoff-bundle</c>, and G195 <c>handoff-bundle-inspect</c> under the same
/// <c>tasking</c> group. It does NOT replace any of those commands.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, and no provider launcher.
/// The associated tests validate the no-provider-launch invariant via the
/// <see cref="NestedProviderLauncher"/> sentinel and a source-scan assertion.
///
/// Exit code: <c>0</c> iff every check in the verification list passes;
/// <c>1</c> otherwise (including missing flag, missing file, parse failure).
/// </summary>
internal static class TaskingHandoffBundleVerifyCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190/G191/G192/G193/G194/G195 <c>NestedProviderLauncher</c>.
    /// G196 must NEVER invoke this delegate. Tests register a sentinel that
    /// flips a flag if invoked; the verify path leaves it untouched.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromBundle, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedFromBundle = Path.GetFullPath(fromBundle);

        // Step 2: file existence — emit single check, do not parse.
        if (!File.Exists(resolvedFromBundle))
        {
            var checks = TaskingHandoffBundleVerifyAnalyzer.BuildPathMissingChecks(fromBundle);
            var result = new TaskingHandoffBundleVerifyResult
            {
                BundlePath = fromBundle,
                BundleSha256 = null,
                Domain = null,
                Valid = false,
                Errors = ExtractErrors(checks),
                Checks = checks,
                SummaryLine = TaskingHandoffBundleVerifyConstants.SummaryLineText
            };
            WriteResult(writer, result, format);
            return 1;
        }

        byte[] bundleBytes;
        try
        {
            bundleBytes = File.ReadAllBytes(resolvedFromBundle);
        }
        catch (Exception exception)
        {
            var checks = TaskingHandoffBundleVerifyAnalyzer.BuildPathMissingChecks(
                $"{fromBundle} (read failure: {exception.Message})");
            var result = new TaskingHandoffBundleVerifyResult
            {
                BundlePath = fromBundle,
                BundleSha256 = null,
                Domain = null,
                Valid = false,
                Errors = ExtractErrors(checks),
                Checks = checks,
                SummaryLine = TaskingHandoffBundleVerifyConstants.SummaryLineText
            };
            WriteResult(writer, result, format);
            return 1;
        }

        var bundleSha256 = IssuePrepareCommand.ComputeSha256Hex(bundleBytes);

        TaskingHandoffBundleArtifact? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(bundleBytes);
        }
        catch (JsonException exception)
        {
            var checks = TaskingHandoffBundleVerifyAnalyzer.BuildJsonParseFailureChecks(
                exception.Message);
            var result = new TaskingHandoffBundleVerifyResult
            {
                BundlePath = fromBundle,
                BundleSha256 = bundleSha256,
                Domain = null,
                Valid = false,
                Errors = ExtractErrors(checks),
                Checks = checks,
                SummaryLine = TaskingHandoffBundleVerifyConstants.SummaryLineText
            };
            WriteResult(writer, result, format);
            return 1;
        }

        if (bundle is null)
        {
            var checks = TaskingHandoffBundleVerifyAnalyzer.BuildJsonParseFailureChecks(
                "deserialized to a null bundle.");
            var result = new TaskingHandoffBundleVerifyResult
            {
                BundlePath = fromBundle,
                BundleSha256 = bundleSha256,
                Domain = null,
                Valid = false,
                Errors = ExtractErrors(checks),
                Checks = checks,
                SummaryLine = TaskingHandoffBundleVerifyConstants.SummaryLineText
            };
            WriteResult(writer, result, format);
            return 1;
        }

        var allChecks = TaskingHandoffBundleVerifyAnalyzer.BuildChecks(bundle);
        var valid = allChecks.All(c => c.Passed);
        var finalResult = new TaskingHandoffBundleVerifyResult
        {
            BundlePath = fromBundle,
            BundleSha256 = bundleSha256,
            Domain = bundle.Domain,
            Valid = valid,
            Errors = ExtractErrors(allChecks),
            Checks = allChecks,
            SummaryLine = TaskingHandoffBundleVerifyConstants.SummaryLineText
        };

        WriteResult(writer, finalResult, format);
        return valid ? 0 : 1;
    }

    private static IReadOnlyList<string> ExtractErrors(IReadOnlyList<VerifyCheck> checks)
    {
        var errors = new List<string>();
        foreach (var check in checks)
        {
            if (!check.Passed)
            {
                errors.Add($"{check.Id}: {check.Detail}");
            }
        }

        return errors;
    }

    private static void WriteResult(
        TextWriter writer,
        TaskingHandoffBundleVerifyResult result,
        string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOutputOptions));
        }
        else
        {
            WriteTextResult(writer, result);
        }
    }

    private static void WriteTextResult(TextWriter writer, TaskingHandoffBundleVerifyResult result)
    {
        writer.WriteLine(result.SummaryLine);
        writer.WriteLine();
        writer.WriteLine($"Bundle path: {result.BundlePath}");
        writer.WriteLine($"Bundle sha256: {result.BundleSha256 ?? "(unavailable)"}");
        writer.WriteLine($"Domain: {result.Domain ?? "(unavailable)"}");
        writer.WriteLine($"Valid: {result.Valid}");
        writer.WriteLine();
        writer.WriteLine("Checks:");
        foreach (var check in result.Checks)
        {
            writer.WriteLine($"- {check.Id}: passed={check.Passed} (detail: {check.Detail})");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromBundle,
        out string format,
        out string error)
    {
        fromBundle = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-bundle":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-bundle requires a non-empty value.";
                        return false;
                    }

                    fromBundle = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error =
                        $"Unknown argument '{argument}'. Supported: --from-bundle, --format.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromBundle))
        {
            error = "--from-bundle is required.";
            return false;
        }

        return true;
    }
}
