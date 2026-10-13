using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G868's opt-in worker-completion evidence observation. This adapter reuses
/// the typed G867 reader and the existing live PR-head reader; it does not
/// authenticate who produced a report or observe command execution.
/// </summary>
internal static class WorkerVerificationGate
{
    internal const string Marker = "--require-verification";
    internal const string Guarantee = "observed-before-first-lifecycle-write-not-atomic";

    private static readonly HashSet<string> VerificationValueOptions = new(StringComparer.Ordinal)
    {
        "--verification-unit", "--verification-domain", "--verification-team",
        "--verification-head-sha", "--verification-packet-sha256", "--evidence",
    };

    private static readonly HashSet<string> LegacyValueOptions = new(StringComparer.Ordinal)
    {
        "--repo", "--domain", "--kind", "--number", "--outcome", "--pr",
        "--accept-evidence-gap", "--format",
    };

    private static readonly HashSet<string> LegacySingletonOptions = new(StringComparer.Ordinal)
    {
        "--repo", "--domain", "--kind", "--number", "--outcome", "--pr",
        "--accept-evidence-gap", "--format", "--write", "--dry-run",
        "--child-cwd", "--github-only",
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed record Invocation(
        bool IsOptIn,
        string[] LegacyArguments,
        string Format,
        VerificationEvidenceCoverage.Options? CoverageOptions,
        string? Error);

    internal sealed record Dependencies(
        Func<string, int, string?>? ReadPullRequestHeadSha = null,
        Action<string, string>? BeforeCoverageOperation = null);

    internal sealed record Result(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("cause")] string Cause,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("phase")] string Phase,
        [property: JsonPropertyName("target_pr")] int? TargetPr,
        [property: JsonPropertyName("requested_head_sha")] string? RequestedHeadSha,
        [property: JsonPropertyName("observed_head_sha")] string? ObservedHeadSha,
        [property: JsonPropertyName("coverage")] VerificationEvidenceCoverage.Report? Coverage,
        [property: JsonPropertyName("guarantee")] string Guarantee);

    internal sealed record InvalidArgumentsEnvelope(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("proceed")] bool Proceed,
        [property: JsonPropertyName("applied")] bool Applied,
        [property: JsonPropertyName("verification_gate")] Result VerificationGate);

    internal static Invocation ParseInvocation(string[] args)
    {
        if (!HasMarker(args))
        {
            return new(false, args, "text", null, null);
        }

        var format = SelectFormat(args);
        var legacy = new List<string>(args.Length);
        var coverageArgs = new List<string>();
        var seenLegacy = new HashSet<string>(StringComparer.Ordinal);
        var seenVerification = new HashSet<string>(StringComparer.Ordinal);
        string? repo = null;
        string? error = null;
        var markerCount = 0;
        var write = false;
        var dryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == Marker)
            {
                markerCount++;
                continue;
            }

            if (VerificationValueOptions.Contains(argument))
            {
                if (index + 1 >= args.Length || string.IsNullOrEmpty(args[index + 1])
                    || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    error ??= $"{argument} requires a nonempty value";
                    continue;
                }

                var value = args[++index];
                if (argument == "--evidence")
                {
                    coverageArgs.Add(argument);
                    coverageArgs.Add(value);
                    continue;
                }
                if (!seenVerification.Add(argument))
                {
                    error ??= $"duplicate argument '{argument}'";
                    continue;
                }

                coverageArgs.Add(argument switch
                {
                    "--verification-unit" => "--execution-unit",
                    "--verification-domain" => "--domain",
                    "--verification-team" => "--team",
                    "--verification-head-sha" => "--head-sha",
                    "--verification-packet-sha256" => "--packet-sha256",
                    _ => argument,
                });
                coverageArgs.Add(value);
                continue;
            }

            if (LegacyValueOptions.Contains(argument))
            {
                if (index + 1 >= args.Length || string.IsNullOrEmpty(args[index + 1])
                    || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    error ??= $"{argument} requires a nonempty value";
                    legacy.Add(argument);
                    continue;
                }
                if (!seenLegacy.Add(argument)) error ??= $"duplicate argument '{argument}'";
                var value = args[++index];
                if (argument == "--repo") repo = value;
                if (argument == "--format" && value is not ("text" or "json" or "markdown"))
                    error ??= "--format must be text, json, or markdown";
                legacy.Add(argument);
                // The legacy parser predates Markdown. Parse its ordinary
                // fields with JSON internally, then render the opt-in result
                // using the selected G868 format below.
                legacy.Add(argument == "--format" && value == "markdown" ? "json" : value);
                continue;
            }

            if (LegacySingletonOptions.Contains(argument))
            {
                if (!seenLegacy.Add(argument)) error ??= $"duplicate argument '{argument}'";
                if (argument == "--write") write = true;
                if (argument == "--dry-run") dryRun = true;
                legacy.Add(argument);
                continue;
            }

            // Preserve the legacy parser's established unknown-option
            // diagnostics, but project them through the G868 arguments
            // envelope because the marker selected the new contract.
            legacy.Add(argument);
        }

        if (markerCount != 1) error ??= $"{Marker} must appear exactly once";
        if (write && dryRun) error ??= "--write and --dry-run are mutually exclusive";

        if (repo is not null)
        {
            coverageArgs.Add("--repo");
            coverageArgs.Add(repo);
        }
        var options = VerificationEvidenceCoverage.ParseArguments(coverageArgs.ToArray());
        error ??= options.Error;
        if (error is not null)
        {
            return new(true, legacy.ToArray(), format, options, error);
        }

        return new(true, legacy.ToArray(), format, options, null);
    }

    internal static Result EvaluateCoverage(
        string repoRoot,
        VerificationEvidenceCoverage.Options options,
        string phase,
        int targetPr,
        string requestedHeadSha,
        Dependencies? dependencies)
    {
        VerificationEvidenceCoverage.Report report;
        try
        {
            report = VerificationEvidenceCoverage.Evaluate(
                repoRoot, options, dependencies?.BeforeCoverageOperation);
        }
        catch (Exception exception) when (IsBoundedReadFailure(exception))
        {
            _ = exception;
            report = UnavailableCoverage(options);
        }

        var classification = Classify(report);
        return new(classification.State, classification.Cause, classification.Detail,
            phase, targetPr, requestedHeadSha, null, report, Guarantee);
    }

    internal static Result CheckHead(
        string repo,
        int targetPr,
        string requestedHeadSha,
        string phase,
        VerificationEvidenceCoverage.Report? coverage,
        Dependencies? dependencies)
    {
        string? observed;
        try
        {
            observed = (dependencies?.ReadPullRequestHeadSha
                ?? GhCliGitHubLabelMutator.ReadPullRequestHeadSha)(repo, targetPr);
        }
        catch (Exception exception) when (IsBoundedReadFailure(exception))
        {
            _ = exception;
            return new("unavailable", "worker-verification-head-unavailable",
                "the target pull request head could not be observed safely", phase,
                targetPr, requestedHeadSha, null, coverage, Guarantee);
        }

        if (!VerificationEvidenceCommand.IsLowerHex(observed, 40))
        {
            return new("unavailable", "worker-verification-head-unavailable",
                "the target pull request head was missing or malformed", phase,
                targetPr, requestedHeadSha, null, coverage, Guarantee);
        }
        if (!string.Equals(observed, requestedHeadSha, StringComparison.Ordinal))
        {
            return new("refused", "worker-verification-head-mismatch",
                "the observed target pull request head does not match the requested evidence head",
                phase, targetPr, requestedHeadSha, observed, coverage, Guarantee);
        }
        return new("satisfied", "worker-verification-satisfied",
            "the target pull request head matches the requested evidence head",
            phase, targetPr, requestedHeadSha, observed, coverage, Guarantee);
    }

    internal static Result IdentityConflict(
        int targetPr,
        string requestedHeadSha,
        string detail,
        string phase,
        VerificationEvidenceCoverage.Report? coverage = null,
        string? observedHeadSha = null) =>
        new("refused", "worker-verification-identity-conflict", detail, phase,
            targetPr, requestedHeadSha, observedHeadSha, coverage, Guarantee);

    internal static Result RecheckFailure(
        Result previous,
        Result headResult,
        VerificationEvidenceCoverage.Options options,
        string repoRoot,
        int targetPr,
        string requestedHeadSha,
        Dependencies? dependencies)
    {
        if (headResult.State != "satisfied") return headResult with { Coverage = null };
        var coverage = EvaluateCoverage(repoRoot, options, "pre-write", targetPr, requestedHeadSha, dependencies);
        if (coverage.State == "satisfied") return coverage;
        _ = previous;
        return coverage with { ObservedHeadSha = headResult.ObservedHeadSha };
    }

    internal static Result RefusedInitialCoverage(Result result) => result with { ObservedHeadSha = null };

    internal static string WriteArgumentRefusal(TextWriter writer, string format, string detail)
    {
        var gate = new Result("invalid", "worker-verification-invalid-arguments",
            Bound(detail), "arguments", null, null, null, null, Guarantee);
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(new InvalidArgumentsEnvelope(
                "worker complete", false, false, gate), JsonOptions));
        }
        else
        {
            writer.WriteLine("# Worker complete verification gate");
            writer.WriteLine();
            writer.WriteLine("- command: worker complete");
            writer.WriteLine("- proceed: false");
            writer.WriteLine("- applied: false");
            writer.WriteLine("- state: invalid");
            writer.WriteLine("- cause: worker-verification-invalid-arguments");
            writer.WriteLine("- phase: arguments");
            writer.WriteLine("- target_pr: (unknown)");
            writer.WriteLine("- requested_head_sha: (unknown)");
            writer.WriteLine("- observed_head_sha: (unknown)");
            writer.WriteLine("- coverage: (unknown)");
            WriteFence(writer, "text", gate.Detail);
            writer.WriteLine($"- guarantee: {Guarantee}");
        }
        return format;
    }

    internal static void WriteText(TextWriter writer, Result gate)
    {
        writer.WriteLine("## Verification gate");
        writer.WriteLine($"- state: {gate.State}");
        writer.WriteLine($"- cause: {gate.Cause}");
        writer.WriteLine($"- phase: {gate.Phase}");
        writer.WriteLine($"- target_pr: {gate.TargetPr?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(unknown)"}");
        writer.WriteLine($"- requested_head_sha: {gate.RequestedHeadSha ?? "(unknown)"}");
        writer.WriteLine($"- observed_head_sha: {gate.ObservedHeadSha ?? "(unknown)"}");
        writer.WriteLine("- detail:");
        WriteFence(writer, "text", gate.Detail);
        writer.WriteLine($"- guarantee: {gate.Guarantee}");
        if (gate.Coverage is not null)
        {
            writer.WriteLine("- coverage:");
            WriteFence(writer, "json", JsonSerializer.Serialize(gate.Coverage, JsonOptions));
        }
    }

    internal static void WriteFence(TextWriter writer, string language, string value)
    {
        var longest = 0;
        var current = 0;
        foreach (var character in value)
        {
            if (character == '`') { current++; longest = Math.Max(longest, current); }
            else current = 0;
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        writer.WriteLine(fence + language);
        writer.WriteLine(value);
        writer.WriteLine(fence);
    }

    internal static bool IsSupportedOutcome(string kind, string outcome) =>
        (kind == GhCliGitHubLabelMutator.Kinds.Issue
            && outcome == WorkerResultSummaryConstants.Outcomes.PrCreated)
        || (kind == GhCliGitHubLabelMutator.Kinds.Pr
            && outcome == WorkerResultSummaryConstants.Outcomes.RepairPushed);

    private static bool HasMarker(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == Marker) return true;
            if (LegacyValueOptions.Contains(argument)
                && index + 1 < args.Length)
            {
                index++;
            }
        }
        return false;
    }

    private static string SelectFormat(string[] args)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (args[index] == "--format" && args[index + 1] is "json" or "markdown")
                return args[index + 1];
        }
        return "text";
    }

    private static (string State, string Cause, string Detail) Classify(VerificationEvidenceCoverage.Report report)
    {
        if (report.State is "invalid" or "unavailable")
            return ("unavailable", "worker-verification-coverage-unavailable", CoverageDetail(report));

        var local = report.Proofs.Where(proof => proof.Evidence == "local-required").ToArray();
        if (report.Proofs.Any(proof => proof.Evidence == "hosted-required") || local.Length == 0)
        {
            return ("refused", "worker-verification-unsupported-evidence",
                "the local-only worker gate requires at least one local-required proof and no hosted-required proofs");
        }

        if (report.State != "complete" || report.StructuralState != "complete"
            || local.Any(proof => proof.State != "present" || proof.Report is null
                || proof.Report.Passed <= 0 || proof.Report.Failed != 0 || proof.Report.Skipped != 0))
        {
            return ("refused", "worker-verification-coverage-incomplete", CoverageDetail(report));
        }

        return ("satisfied", "worker-verification-satisfied",
            "typed local verification coverage is structurally complete and current");
    }

    private static string CoverageDetail(VerificationEvidenceCoverage.Report report) => Bound(
        $"typed G867 coverage is {report.State} ({report.Cause}): {report.Detail}");

    private static VerificationEvidenceCoverage.Report UnavailableCoverage(VerificationEvidenceCoverage.Options options)
    {
        const string detail = "a bounded local evidence read could not be completed safely";
        return new(options.Unit, options.Domain, options.Team, options.Repo, options.HeadSha,
            options.PacketSha256, null, null, "unavailable", "verification-coverage-packet-unavailable", detail,
            "not-observed", "not-authenticated", "caller-supplied-not-authenticated", "not-assessed", "not-verified",
            null, [], [], [], [], [], [], [], [],
            [new("verification-coverage-packet-unavailable", null, detail)]);
    }

    private static bool IsBoundedReadFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException;

    private static string Bound(string detail) => detail.Length <= 512 ? detail : detail[..512];
}
