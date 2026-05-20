using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: <c>intent-cli review signal-handled (--issue &lt;n&gt; | --pr &lt;n&gt;) --repo &lt;r&gt;</c>.
/// Host review/design side convergence: once a structured worker signal
/// has been triaged into clarification / packet / metadata-repair work,
/// this adds <c>intent-signal-handled</c> and removes
/// <c>intent-signal-sent</c> so <c>review collect-signals</c> will not
/// re-surface it. GitHub-only by data flow (label mutator seam; no
/// queue-state / packet / intent metadata touched). Dry-run by default.
/// </summary>
internal static class ReviewSignalHandledCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>Test seam: inject a fake label mutator.</summary>
    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

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

        if (!TryParseArguments(args, out var repo, out var target, out var number, out var mode, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub mutator: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationLabel> currentLabels;
        try
        {
            currentLabels = mutator.ReadLabels(repo!, target!, number);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine(
                $"failed to read current labels for {target} #{number} in {repo}: {exception.Message}");
            return 1;
        }

        var currentNames = LabelNames(currentLabels);
        var plan = WorkerSignalContract.PlanHandledTransition(currentNames);
        var hadSent = currentNames.Contains(WorkerSignalContract.Labels.SignalSent, StringComparer.Ordinal);
        var alreadyHandled = currentNames.Contains(WorkerSignalContract.Labels.SignalHandled, StringComparer.Ordinal);

        var warnings = new List<string>();
        if (!hadSent)
        {
            warnings.Add(
                $"{target} #{number} does not carry '{WorkerSignalContract.Labels.SignalSent}'; there is no pending signal to converge.");
        }

        var proceed = plan.HasChanges;
        var applied = false;
        if (proceed && string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            try
            {
                mutator.ApplyLabelTransitions(repo!, target!, number, plan.AddLabels, plan.RemoveLabels);
                applied = true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                writer.WriteLine(
                    $"failed to apply signal-handled transition on {target} #{number} in {repo}: {exception.Message}");
                return 1;
            }
        }

        var summary = !proceed
            ? (alreadyHandled
                ? $"{target} #{number} is already marked '{WorkerSignalContract.Labels.SignalHandled}'; nothing to do."
                : $"{target} #{number} has no pending signal labels; nothing to do.")
            : (string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal)
                ? $"Marked {target} #{number} '{WorkerSignalContract.Labels.SignalHandled}' and cleared '{WorkerSignalContract.Labels.SignalSent}'."
                : $"Dry run: would mark {target} #{number} '{WorkerSignalContract.Labels.SignalHandled}' and clear '{WorkerSignalContract.Labels.SignalSent}'.");

        var result = new ReviewSignalHandledResult
        {
            Repo = repo!,
            Target = target!,
            Number = number,
            Mode = mode,
            Proceed = proceed,
            Applied = applied,
            AddLabels = plan.AddLabels,
            RemoveLabels = plan.RemoveLabels,
            CurrentLabels = currentNames,
            Summary = summary,
            Warnings = warnings,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
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

    private static void WriteText(TextWriter writer, ReviewSignalHandledResult result)
    {
        writer.WriteLine($"# Signal handled: {result.Target} #{result.Number} ({result.Repo})");
        writer.WriteLine();
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- proceed: {result.Proceed}");
        writer.WriteLine($"- applied: {result.Applied}");
        writer.WriteLine($"- add: {(result.AddLabels.Count == 0 ? "(none)" : string.Join(", ", result.AddLabels))}");
        writer.WriteLine($"- remove: {(result.RemoveLabels.Count == 0 ? "(none)" : string.Join(", ", result.RemoveLabels))}");
        writer.WriteLine($"- summary: {result.Summary}");
        writer.WriteLine();
        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? target,
        out int number,
        out string mode,
        out string format,
        out string error)
    {
        repo = null;
        target = null;
        number = 0;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
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

                case "--issue":
                    if (!TryParseTarget(args, ref index, WorkerSignalContract.Targets.Issue, ref target, ref number, out error))
                    {
                        return false;
                    }
                    break;

                case "--pr":
                    if (!TryParseTarget(args, ref index, WorkerSignalContract.Targets.Pr, ref target, ref number, out error))
                    {
                        return false;
                    }
                    break;

                case "--write":
                    mode = WorkerClaimCompleteConstants.Modes.Write;
                    break;

                case "--dry-run":
                    mode = WorkerClaimCompleteConstants.Modes.DryRun;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    index++;
                    break;

                default:
                    error =
                        $"Unknown argument '{argument}'. Supported: --repo <owner/repo> (--issue <n> | --pr <n>) [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(target) || number <= 0)
        {
            error = "exactly one of --issue <n> or --pr <n> is required.";
            return false;
        }

        return true;
    }

    private static bool TryParseTarget(
        string[] args,
        ref int index,
        string targetKind,
        ref string? target,
        ref int number,
        out string error)
    {
        error = string.Empty;
        if (target is not null)
        {
            error = "specify exactly one of --issue or --pr, not both.";
            return false;
        }
        if (index + 1 >= args.Length
            || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out number)
            || number <= 0)
        {
            error = $"--{targetKind} requires a positive integer.";
            return false;
        }
        target = targetKind;
        index++;
        return true;
    }
}
