using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211/G717: <c>intent-cli worker claim</c> command. Applies (or in
/// dry-run mode, describes) the in-progress label transition for a
/// selected issue or PR target — the explicit mutation boundary that
/// replaces ad-hoc label edits in prompts. When an issue's claim registry
/// proves an existing in-progress label stale, the command succeeds as a
/// no-op and leaves the shadow label untouched.
///
/// No-mutation invariants (verified by tests):
/// - dry-run mode (default) NEVER calls
///   <see cref="IGitHubLabelMutator.ApplyLabelTransitions"/>;
/// - this command and its analyzer file contain no
///   <c>Process.Start</c>, no <c>gh</c> mutation literals, and never
///   invoke <see cref="NestedProviderLauncher"/>;
/// - whole-workspace byte-snapshot before/after asserts no file is
///   created or modified;
/// - the only file in the worker claim/complete surface that calls
///   <c>Process.Start</c> is the mutator adapter
///   <see cref="GhCliGitHubLabelMutator"/>, by design.
/// </summary>
internal static class WorkerClaimCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test seam: tests inject a fake <see cref="IGitHubLabelMutator"/>
    /// here so no real GitHub network call is made.
    /// </summary>
    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    /// <summary>
    /// G717 test seam: issue title lookup used to resolve the execution-unit
    /// claim before the lifecycle-label decision. Production callers use the
    /// default GitHub lookup; tests inject a payload so the claim path stays
    /// hermetic.
    /// </summary>
    public static Func<IGitHubIssueLookup>? IssueLookupFactory { get; set; }

    /// <summary>
    /// Test sentinel: must NEVER be invoked. Tests assert it remains
    /// uninvoked across all command paths to lock the "no nested
    /// provider launch" guarantee.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(
                args,
                out var repo,
                out var kind,
                out var number,
                out var mode,
                out var githubOnly,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub mutator: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationLabel> currentLabels;
        try
        {
            currentLabels = mutator.ReadLabels(repo!, kind!, number);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine(
                $"failed to read current labels for {kind} #{number} in {repo}: {exception.Message}");
            return 1;
        }

        var currentNames = LabelNames(currentLabels);
        var claimVerification = ResolveIssueClaimVerification(
            context,
            repo!,
            kind!,
            number);
        var decision = WorkerClaimAnalyzer.Analyze(
            kind!,
            currentNames,
            claimVerification);

        var applied = false;
        if (decision.Proceed
            && (decision.AddLabels.Count > 0 || decision.RemoveLabels.Count > 0)
            && string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            try
            {
                mutator.ApplyLabelTransitions(
                    repo!, kind!, number, decision.AddLabels, decision.RemoveLabels);
                applied = true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or IOException)
            {
                writer.WriteLine(
                    $"failed to apply claim transition on {kind} #{number} in {repo}: {exception.Message}");
                return 1;
            }
        }

        var result = new WorkerClaimResult
        {
            Kind = kind!,
            Repo = repo!,
            Number = number,
            Mode = mode,
            Proceed = decision.Proceed,
            Applied = applied,
            AddLabels = decision.AddLabels,
            RemoveLabels = decision.RemoveLabels,
            CurrentLabels = currentNames,
            Errors = decision.Errors,
            Warnings = decision.Warnings,
            Summary = decision.Summary,
            GithubOnly = githubOnly ? true : null,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return decision.Proceed ? 0 : 2;
    }

    private static IReadOnlyList<string> LabelNames(IReadOnlyList<GitHubAutomationLabel>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(labels.Count);
        foreach (var label in labels)
        {
            if (!string.IsNullOrEmpty(label.Name))
            {
                names.Add(label.Name);
            }
        }
        return names;
    }

    private static ClaimOwnershipVerification? ResolveIssueClaimVerification(
        CliContext context,
        string repo,
        string kind,
        int number)
    {
        if (!string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal))
        {
            return null;
        }

        var store = ClaimOwnershipVerifier.ProbeStore(context.RepoRoot);
        if (!store.Available)
        {
            return ClaimOwnershipVerifier.Unavailable(
                $"execution-unit:issue-{number}",
                $"claim verification refused issue #{number}: fresh canonical Git evidence is unavailable ({store.Detail}).");
        }
        if (!store.StoreConfigured)
        {
            return null;
        }

        try
        {
            var lookup = IssueLookupFactory?.Invoke() ?? new GhCliGitHubIssueLookup();
            var issue = lookup.Lookup(repo, number);
            var match = LeadingExecutionUnitPattern.Match(issue.Title ?? string.Empty);
            if (!match.Success)
            {
                return ClaimOwnershipVerifier.Unavailable(
                    $"execution-unit:issue-{number}",
                    $"claim verification refused issue #{number}: could not resolve a leading execution-unit token from the issue title on a claims-enabled host.");
            }

            return ClaimOwnershipVerifier.Verify(
                context.RepoRoot,
                $"execution-unit:{match.Value}",
                invokingTeam: null,
                allowUnheld: true);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or IOException
            or FormatException)
        {
            return ClaimOwnershipVerifier.Unavailable(
                $"execution-unit:issue-{number}",
                $"claim verification refused issue #{number}: canonical issue/title lookup failed ({exception.Message}).");
        }
    }

    private static readonly Regex LeadingExecutionUnitPattern = new(
        @"^(?:[A-Z][A-Z0-9]*-G?[0-9]+|G[0-9]+)(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    private static void WriteText(TextWriter writer, WorkerClaimResult result)
    {
        writer.WriteLine($"# Worker claim: {result.Kind} #{result.Number} ({result.Repo})");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- proceed: {result.Proceed}");
        writer.WriteLine($"- applied: {result.Applied}");
        writer.WriteLine($"- add: {(result.AddLabels.Count == 0 ? "(none)" : string.Join(", ", result.AddLabels))}");
        writer.WriteLine($"- remove: {(result.RemoveLabels.Count == 0 ? "(none)" : string.Join(", ", result.RemoveLabels))}");
        writer.WriteLine($"- summary: {result.Summary}");
        writer.WriteLine();

        writer.WriteLine("## Errors");
        if (result.Errors.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var item in result.Errors)
            {
                writer.WriteLine($"- {item}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var item in result.Warnings)
            {
                writer.WriteLine($"- {item}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? kind,
        out int number,
        out string mode,
        out bool githubOnly,
        out string format,
        out string error)
    {
        repo = null;
        kind = null;
        number = 0;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
        githubOnly = false;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value ('issue' or 'pr').";
                        return false;
                    }
                    kind = args[index + 1];
                    index++;
                    break;

                case "--number":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out number)
                        || number <= 0)
                    {
                        error = "--number requires a positive integer.";
                        return false;
                    }
                    index++;
                    break;

                case "--write":
                    mode = WorkerClaimCompleteConstants.Modes.Write;
                    break;

                case "--dry-run":
                    mode = WorkerClaimCompleteConstants.Modes.DryRun;
                    break;

                case "--github-only":
                    // G333: strict child-loop assertion. `worker claim`
                    // is already GitHub-label-only by data flow (uses
                    // IGitHubLabelMutator; no queue-state read/write).
                    // The flag records the binding on the result so
                    // the host loop can audit.
                    githubOnly = true;
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
                        $"Unknown argument '{argument}'. Supported: --repo <owner/repo> --kind <issue|pr> --number <N> [--write] [--dry-run] [--github-only] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind is required ('issue' or 'pr').";
            return false;
        }
        if (!string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal)
            && !string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal))
        {
            error = $"--kind must be 'issue' or 'pr' (got '{kind}').";
            return false;
        }
        if (number <= 0)
        {
            error = "--number is required and must be a positive integer.";
            return false;
        }

        return true;
    }
}
