using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

using DryRunConstants = TaskingHandoffBundleImportDryRunConstants;

/// <summary>
/// G198: <c>intent-cli tasking handoff-bundle-import-dry-run</c>. A LOCAL
/// dry-run import/restore plan emitter that consumes a G194
/// <see cref="TaskingHandoffBundleArtifact"/> handoff bundle artifact (JSON),
/// runs the existing G196/G197
/// <see cref="TaskingHandoffBundleVerifyAnalyzer"/> verification, and prints
/// either a deterministic restore plan (status=ok) or a verify-failure status
/// (status=verify_failed/missing_bundle/malformed_bundle) to STDOUT.
///
/// <para>
/// The command performs no GitHub network calls, applies no labels, launches
/// no provider processes, does NOT touch <c>.intent-cli/queue-state.json</c>
/// or <c>.intent-cli/runs.jsonl</c>, does NOT create branches or worktrees,
/// and does NOT write any artifact file — even on the valid restore-plan
/// path. The restore plan is descriptive only; the bundle does not carry
/// artifact bytes, only paths and sha256 values, so the listed dry-run
/// actions are a contractual placeholder for a future real restore
/// (out-of-scope this slice). See
/// <see cref="DryRunConstants.RestorePlanDisclaimer"/>.
/// </para>
///
/// Sits beside G190 <c>handoff</c>, G191 <c>task-packet</c>, G192
/// <c>task-packet-preview</c>, G193 <c>task-packet-checklist</c>, G194
/// <c>handoff-bundle</c>, G195 <c>handoff-bundle-inspect</c>, and G196/G197
/// <c>handoff-bundle-verify</c> under the same <c>tasking</c> group. It does
/// NOT replace any of those commands.
///
/// Network-mutation invariance: this command's hot path contains no
/// <c>Process.Start</c>, no shell-out to <c>gh</c>, no <c>git</c> shell-out,
/// and no provider launcher. The associated tests validate the
/// no-provider-launch invariant via the <see cref="NestedProviderLauncher"/>
/// sentinel and a source-scan assertion.
///
/// Exit code: <c>0</c> iff the bundle parses, verify reports
/// <c>valid == true</c>, and the restore plan was successfully built;
/// <c>1</c> otherwise (including missing flag, missing file, parse failure,
/// any verify check failure).
/// </summary>
internal static class TaskingHandoffBundleImportDryRunCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Test seam mirroring G190/G191/G192/G193/G194/G195/G196 <c>NestedProviderLauncher</c>.
    /// G198 must NEVER invoke this delegate. Tests register a sentinel that
    /// flips a flag if invoked; the dry-run path leaves it untouched.
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

        if (!File.Exists(resolvedFromBundle))
        {
            var missingResult = new TaskingHandoffBundleImportDryRunResult
            {
                BundlePath = fromBundle,
                BundleSha256 = null,
                Domain = null,
                Status = DryRunConstants.Statuses.MissingBundle,
                Errors = new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Bundle path does not exist: {0}",
                        fromBundle)
                },
                VerifySummary = new TaskingHandoffBundleImportDryRunVerifySummary
                {
                    Valid = false,
                    FailedCheckIds = new[]
                    {
                        TaskingHandoffBundleVerifyConstants.CheckIds.BundlePathPresentAndReadable
                    }
                },
                RestorePlan = Array.Empty<RestorePlanEntry>(),
                RestorePlanDisclaimer = DryRunConstants.RestorePlanDisclaimer,
                SummaryLine = BuildSummaryLine(domain: null, status: DryRunConstants.Statuses.MissingBundle)
            };
            WriteResult(writer, missingResult, format);
            return 1;
        }

        byte[] bundleBytes;
        try
        {
            bundleBytes = File.ReadAllBytes(resolvedFromBundle);
        }
        catch (Exception exception)
        {
            var readFailResult = new TaskingHandoffBundleImportDryRunResult
            {
                BundlePath = fromBundle,
                BundleSha256 = null,
                Domain = null,
                Status = DryRunConstants.Statuses.MissingBundle,
                Errors = new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Bundle path could not be read: {0} ({1})",
                        fromBundle,
                        exception.Message)
                },
                VerifySummary = new TaskingHandoffBundleImportDryRunVerifySummary
                {
                    Valid = false,
                    FailedCheckIds = new[]
                    {
                        TaskingHandoffBundleVerifyConstants.CheckIds.BundlePathPresentAndReadable
                    }
                },
                RestorePlan = Array.Empty<RestorePlanEntry>(),
                RestorePlanDisclaimer = DryRunConstants.RestorePlanDisclaimer,
                SummaryLine = BuildSummaryLine(domain: null, status: DryRunConstants.Statuses.MissingBundle)
            };
            WriteResult(writer, readFailResult, format);
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
            var malformedResult = new TaskingHandoffBundleImportDryRunResult
            {
                BundlePath = fromBundle,
                BundleSha256 = bundleSha256,
                Domain = null,
                Status = DryRunConstants.Statuses.MalformedBundle,
                Errors = new[]
                {
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Failed to parse bundle JSON as TaskingHandoffBundleArtifact: {0}",
                        exception.Message)
                },
                VerifySummary = new TaskingHandoffBundleImportDryRunVerifySummary
                {
                    Valid = false,
                    FailedCheckIds = new[]
                    {
                        TaskingHandoffBundleVerifyConstants.CheckIds.BundleJsonParses
                    }
                },
                RestorePlan = Array.Empty<RestorePlanEntry>(),
                RestorePlanDisclaimer = DryRunConstants.RestorePlanDisclaimer,
                SummaryLine = BuildSummaryLine(domain: null, status: DryRunConstants.Statuses.MalformedBundle)
            };
            WriteResult(writer, malformedResult, format);
            return 1;
        }

        if (bundle is null)
        {
            var nullResult = new TaskingHandoffBundleImportDryRunResult
            {
                BundlePath = fromBundle,
                BundleSha256 = bundleSha256,
                Domain = null,
                Status = DryRunConstants.Statuses.MalformedBundle,
                Errors = new[] { "Bundle JSON deserialized to a null bundle." },
                VerifySummary = new TaskingHandoffBundleImportDryRunVerifySummary
                {
                    Valid = false,
                    FailedCheckIds = new[]
                    {
                        TaskingHandoffBundleVerifyConstants.CheckIds.BundleJsonParses
                    }
                },
                RestorePlan = Array.Empty<RestorePlanEntry>(),
                RestorePlanDisclaimer = DryRunConstants.RestorePlanDisclaimer,
                SummaryLine = BuildSummaryLine(domain: null, status: DryRunConstants.Statuses.MalformedBundle)
            };
            WriteResult(writer, nullResult, format);
            return 1;
        }

        // Reuse G197 file observation + G196/G197 analyzer, same as
        // TaskingHandoffBundleVerifyCommand.
        var observations =
            TaskingHandoffBundleVerifyCommand.ObserveReferencedSourceArtifacts(bundle);
        var verifyChecks = TaskingHandoffBundleVerifyAnalyzer.BuildChecks(bundle, observations);
        var valid = verifyChecks.All(c => c.Passed);

        var failedCheckIds =
            TaskingHandoffBundleImportDryRunAnalyzer.ExtractFailedCheckIds(verifyChecks);

        if (!valid)
        {
            var verifyFailedResult = new TaskingHandoffBundleImportDryRunResult
            {
                BundlePath = fromBundle,
                BundleSha256 = bundleSha256,
                Domain = bundle.Domain,
                Status = DryRunConstants.Statuses.VerifyFailed,
                Errors = ExtractErrorMessages(verifyChecks),
                VerifySummary = new TaskingHandoffBundleImportDryRunVerifySummary
                {
                    Valid = false,
                    FailedCheckIds = failedCheckIds
                },
                RestorePlan = Array.Empty<RestorePlanEntry>(),
                RestorePlanDisclaimer = DryRunConstants.RestorePlanDisclaimer,
                SummaryLine = BuildSummaryLine(bundle.Domain, DryRunConstants.Statuses.VerifyFailed)
            };
            WriteResult(writer, verifyFailedResult, format);
            return 1;
        }

        var restorePlan = TaskingHandoffBundleImportDryRunAnalyzer.BuildRestorePlan(
            bundle,
            verifyChecks);

        var okResult = new TaskingHandoffBundleImportDryRunResult
        {
            BundlePath = fromBundle,
            BundleSha256 = bundleSha256,
            Domain = bundle.Domain,
            Status = DryRunConstants.Statuses.Ok,
            Errors = Array.Empty<string>(),
            VerifySummary = new TaskingHandoffBundleImportDryRunVerifySummary
            {
                Valid = true,
                FailedCheckIds = Array.Empty<string>()
            },
            RestorePlan = restorePlan,
            RestorePlanDisclaimer = DryRunConstants.RestorePlanDisclaimer,
            SummaryLine = BuildSummaryLine(bundle.Domain, DryRunConstants.Statuses.Ok)
        };

        WriteResult(writer, okResult, format);
        return 0;
    }

    private static IReadOnlyList<string> ExtractErrorMessages(IReadOnlyList<VerifyCheck> checks)
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

    private static string BuildSummaryLine(string? domain, string status)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            DryRunConstants.SummaryLineFormat,
            domain ?? "(unavailable)",
            status);
    }

    private static void WriteResult(
        TextWriter writer,
        TaskingHandoffBundleImportDryRunResult result,
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

    private static void WriteTextResult(
        TextWriter writer,
        TaskingHandoffBundleImportDryRunResult result)
    {
        writer.WriteLine(result.SummaryLine);
        writer.WriteLine();
        writer.WriteLine($"Bundle path: {result.BundlePath}");
        writer.WriteLine($"Bundle sha256: {result.BundleSha256 ?? "(unavailable)"}");
        writer.WriteLine($"Domain: {result.Domain ?? "(unavailable)"}");
        writer.WriteLine($"Status: {result.Status}");
        writer.WriteLine($"Verify valid: {result.VerifySummary.Valid}");

        if (result.VerifySummary.FailedCheckIds.Count > 0)
        {
            writer.WriteLine("Failed verify check ids:");
            foreach (var id in result.VerifySummary.FailedCheckIds)
            {
                writer.WriteLine($"- {id}");
            }
        }

        if (result.Errors.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Errors:");
            foreach (var err in result.Errors)
            {
                writer.WriteLine($"- {err}");
            }
        }

        writer.WriteLine();
        if (result.RestorePlan.Count > 0)
        {
            writer.WriteLine("Restore plan (DRY-RUN — no action performed):");
            foreach (var entry in result.RestorePlan)
            {
                writer.WriteLine($"- role: {entry.Role}");
                writer.WriteLine($"  recorded_path: {entry.RecordedPath}");
                writer.WriteLine($"  recorded_sha256: {entry.RecordedSha256}");
                writer.WriteLine($"  current_status: {entry.CurrentStatus}");
                writer.WriteLine($"  dry_run_action: {entry.DryRunAction}");
            }
        }
        else
        {
            writer.WriteLine("Restore plan: (empty — bundle did not pass verify)");
        }

        writer.WriteLine();
        writer.WriteLine(result.RestorePlanDisclaimer);
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
