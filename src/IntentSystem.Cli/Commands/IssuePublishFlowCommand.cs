using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G245: <c>intent-cli issue publish-flow</c> command. Performs the
/// deterministic issue create / durable-publish-boundary handoff for an
/// existing packet without prompt-specific label mutation knowledge.
///
/// Validates the packet's <c>github-body.md</c> for required Child Issue
/// Contract sections before any GitHub mutation. With <c>--write</c>,
/// creates the GitHub issue WITHOUT <c>intent-target</c>, then atomically
/// reflects the creation in parent durable artifacts (G278): the
/// matching <c>queue-state.json</c> item gets a populated
/// <c>LinkedIssue</c>, <c>publish.yaml</c> advances to
/// <c>issue-created</c> with the GitHub URL/number, and an
/// <c>issue-created</c> event is appended to <c>runs.jsonl</c>. Re-running
/// after a successful publish is idempotent: the <c>publish.yaml</c>
/// issue-created marker short-circuits both the GitHub call and the
/// durable-state writes. The command never applies <c>intent-target</c>
/// directly. Never launches an AI provider.
/// </summary>
internal static class IssuePublishFlowCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    public const string PublishStatusIssueCreated = "issue-created";

    public const string IssueCreatedEventName = "issue-created";

    public const string IssueCreatedEventBy = "issue-publish-flow";

    private static readonly Regex IssueUrlNumberRegex = new(
        @"/issues/(?<number>\d+)(?:[/#?].*)?$",
        RegexOptions.Compiled);

    private const string UsageLine =
        "Usage: intent-cli issue publish-flow <execution-unit> --repo <owner/repo> [--domain <name>] [--write] [--format json|markdown]";

    private static readonly Regex ExecutionUnitPattern = new(
        @"^[A-Za-z][A-Za-z0-9-]*$",
        RegexOptions.Compiled);

    /// <summary>Test seam — replaces the default <c>gh issue create</c> shell out.</summary>
    public static Func<IIssueCreator>? CreatorFactory { get; set; }

    /// <summary>G278: test seam for the durable-state event timestamp.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

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

        if (!TryParseArguments(args, out var executionUnit, out var repo, out var domainOverride, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!ExecutionUnitPattern.IsMatch(executionUnit!))
        {
            writer.WriteLine($"Invalid execution-unit id '{executionUnit}'. Expected an alphanumeric token like 'G245'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit!);
        var githubBodyPath = Path.Combine(packetDirectory, "github-body.md");
        var publishYamlPath = Path.Combine(context.RepoRoot, IssuePublishArtifactPathResolver.Resolve(executionUnit!));

        if (!Directory.Exists(packetDirectory))
        {
            var earlyResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: false,
                githubBodyPresent: false,
                missingSections: PacketDraftCommand.RequiredContractSections,
                title: null,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"packet directory not found: {packetDirectory}");
            EmitResult(writer, earlyResult, format);
            return 1;
        }

        var githubBodyPresent = File.Exists(githubBodyPath);
        IReadOnlyList<string> missing = githubBodyPresent
            ? PacketDraftCommand.RequiredContractSections
                .Where(section => !ContainsSectionHeading(File.ReadAllText(githubBodyPath), section))
                .ToArray()
            : PacketDraftCommand.RequiredContractSections;

        // G290: prefer `packet.yaml` `title:` over the body H1 fallback so a
        // packet with valid metadata never publishes as `<id> (untitled)`.
        // Body H1 remains a fallback for older packets / Sekiban-style
        // bodies that start at `## Goal`. Title source is reported on the
        // result so the operator can audit which path resolved the title.
        // G298: format the resolved title as `<execution-unit> <title>` when
        // the title doesn't already begin with the execution-unit token, so
        // `gh issue create` always carries a scan-friendly identifier in the
        // GitHub issue list. Already-prefixed titles (and the deterministic
        // `<id> (untitled)` fallback) stay verbatim.
        string? title = null;
        string? titleSource = null;
        if (githubBodyPresent)
        {
            (title, titleSource) = ResolveTitleWithSource(executionUnit!, packetDirectory, githubBodyPath);
            title = FormatIssueTitle(executionUnit!, title);
        }

        if (!githubBodyPresent || missing.Count > 0)
        {
            var validationResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: githubBodyPresent,
                missingSections: missing,
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: githubBodyPresent
                    ? "Child Issue Contract is incomplete; required sections are missing."
                    : "github-body.md is missing in the packet directory.",
                titleSource: titleSource);
            EmitResult(writer, validationResult, format);
            return 1;
        }

        if (!write)
        {
            var dryRunResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: null,
                titleSource: titleSource);
            EmitResult(writer, dryRunResult, format);
            return 0;
        }

        var queueStatePathForIdempotency = context.GetQueueStatePath();

        // G278 idempotency: if publish.yaml already records issue-created with a
        // URL, do NOT call gh again and do NOT re-mutate durable state.
        if (TryReadExistingIssueCreatedArtifact(publishYamlPath, out var existing))
        {
            var idempotentResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: true,
                durableStateSynced: true,
                issueUrl: existing.CreatedIssueUrl,
                issueNumber: existing.CreatedIssueNumber,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: null,
                titleSource: titleSource);
            EmitResult(writer, idempotentResult, format);
            return 0;
        }

        // G278 idempotency (PR #660 follow-up): if publish.yaml is missing or stale
        // but queue-state.json already records linked_issue for this execution
        // unit, treat as idempotent so a rerun cannot create a duplicate GitHub
        // issue. Both artifact-level checks must short-circuit before any gh call.
        if (TryReadExistingQueueStateLinkedIssue(queueStatePathForIdempotency, executionUnit!, out var queueLinkedIssue))
        {
            var queueIdempotentResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: true,
                durableStateSynced: true,
                issueUrl: queueLinkedIssue.Url,
                issueNumber: queueLinkedIssue.Number,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: null,
                titleSource: titleSource);
            EmitResult(writer, queueIdempotentResult, format);
            return 0;
        }

        IIssueCreator creator;
        try
        {
            creator = CreatorFactory?.Invoke() ?? new GhCliIssueCreator();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            var creatorErrorResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"failed to initialize GitHub issue creator: {exception.Message}",
                titleSource: titleSource);
            EmitResult(writer, creatorErrorResult, format);
            return 1;
        }

        IssueCreateOutcome outcome;
        try
        {
            outcome = creator.CreateIssue(repo!, title!, githubBodyPath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            var createErrorResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"gh issue create failed: {exception.Message}",
                titleSource: titleSource);
            EmitResult(writer, createErrorResult, format);
            return 1;
        }

        var issueNumber = ParseIssueNumber(outcome.IssueUrl);
        var queueStatePath = context.GetQueueStatePath();
        var runLogPath = context.GetRunLogPath();
        var publishedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

        bool queueStatePatched;
        bool publishYamlPatched;
        bool runsAppended;
        try
        {
            queueStatePatched = TryPatchQueueStateLinkedIssue(
                queueStatePath,
                executionUnit!,
                repo!,
                issueNumber,
                outcome.IssueUrl,
                publishedAt);

            publishYamlPatched = WritePublishArtifact(
                publishYamlPath,
                packetDirectory,
                githubBodyPath,
                executionUnit!,
                issueNumber,
                outcome.IssueUrl);

            runsAppended = AppendIssueCreatedRunEvent(
                runLogPath,
                executionUnit!,
                repo!,
                issueNumber,
                outcome.IssueUrl,
                publishedAt);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            var partialResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                // G278: do not claim created:true while local durable artifacts remain unmodified.
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: outcome.IssueUrl,
                issueNumber: issueNumber,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"GitHub issue {outcome.IssueUrl} was created but parent durable state could not be updated: {exception.Message}. Reconcile via 'intent-cli automation reconcile' before re-running.",
                titleSource: titleSource);
            EmitResult(writer, partialResult, format);
            return 1;
        }

        // G278 follow-up (PR #660 review): all three durable artifacts must be in
        // sync for success. If any required artifact was not patched (most
        // commonly the queue-state item, e.g. queue-state.json missing or the
        // execution unit absent), refuse to claim created:true.
        if (!queueStatePatched || !publishYamlPatched || !runsAppended)
        {
            var unsynchronizedReasons = new List<string>();
            if (!queueStatePatched)
            {
                unsynchronizedReasons.Add(
                    File.Exists(queueStatePath)
                        ? $"queue-state.json has no item with execution_unit '{executionUnit}'"
                        : $"queue-state.json not found at {queueStatePath}");
            }
            if (!publishYamlPatched)
            {
                unsynchronizedReasons.Add($"publish.yaml at {publishYamlPath} was not written");
            }
            if (!runsAppended)
            {
                unsynchronizedReasons.Add($"runs.jsonl at {runLogPath} did not receive the issue-created event");
            }

            var unsynchronizedResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: outcome.IssueUrl,
                issueNumber: issueNumber,
                queueStatePatched: queueStatePatched,
                publishYamlPatched: publishYamlPatched,
                runsAppended: runsAppended,
                titleSource: titleSource,
                error: $"GitHub issue {outcome.IssueUrl} was created but parent durable state is not fully synchronized: {string.Join("; ", unsynchronizedReasons)}. Reconcile via 'intent-cli automation reconcile' or seed the missing parent artifact, then re-run.");
            EmitResult(writer, unsynchronizedResult, format);
            return 1;
        }

        var successResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
            packetExists: true,
            githubBodyPresent: true,
            missingSections: Array.Empty<string>(),
            title: title,
            created: true,
            idempotent: false,
            durableStateSynced: true,
            issueUrl: outcome.IssueUrl,
            issueNumber: issueNumber,
            queueStatePatched: queueStatePatched,
            publishYamlPatched: publishYamlPatched,
            runsAppended: runsAppended,
            error: null,
            titleSource: titleSource);
        EmitResult(writer, successResult, format);
        return 0;
    }

    private static bool TryReadExistingIssueCreatedArtifact(string publishYamlPath, out IssuePublishArtifact existing)
    {
        existing = default!;
        if (!File.Exists(publishYamlPath))
        {
            return false;
        }

        try
        {
            var artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishYamlPath));
            if (string.Equals(artifact.PublishStatus, PublishStatusIssueCreated, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(artifact.CreatedIssueUrl))
            {
                existing = artifact;
                return true;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // fall through; treat as no existing publish artifact
        }

        return false;
    }

    private static bool TryReadExistingQueueStateLinkedIssue(
        string queueStatePath,
        string executionUnit,
        out LinkedIssue linkedIssue)
    {
        linkedIssue = default!;
        if (!File.Exists(queueStatePath))
        {
            return false;
        }

        try
        {
            var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            foreach (var item in queueState.Items)
            {
                if (!string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
                {
                    continue;
                }

                if (item.LinkedIssue is { } existing && !string.IsNullOrWhiteSpace(existing.Url))
                {
                    linkedIssue = existing;
                    return true;
                }

                break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // fall through; treat as no idempotency hit
        }

        return false;
    }

    private static int? ParseIssueNumber(string issueUrl)
    {
        if (string.IsNullOrWhiteSpace(issueUrl))
        {
            return null;
        }

        var match = IssueUrlNumberRegex.Match(issueUrl.Trim());
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(
            match.Groups["number"].Value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number)
            ? number
            : null;
    }

    private static bool TryPatchQueueStateLinkedIssue(
        string queueStatePath,
        string executionUnit,
        string repo,
        int? issueNumber,
        string issueUrl,
        DateTimeOffset publishedAt)
    {
        if (!File.Exists(queueStatePath))
        {
            return false;
        }

        var raw = File.ReadAllText(queueStatePath);
        var queueState = QueueStateSerializer.Deserialize(raw);

        var matchedIndex = -1;
        for (var index = 0; index < queueState.Items.Count; index++)
        {
            if (string.Equals(queueState.Items[index].ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                matchedIndex = index;
                break;
            }
        }

        if (matchedIndex < 0)
        {
            return false;
        }

        var existingItem = queueState.Items[matchedIndex];
        var updatedItem = existingItem with
        {
            LinkedIssue = new LinkedIssue
            {
                Repo = repo,
                Number = issueNumber,
                Url = issueUrl,
            }
        };

        var newItems = queueState.Items.ToArray();
        newItems[matchedIndex] = updatedItem;

        var updatedState = queueState with
        {
            Items = newItems,
            UpdatedAt = publishedAt,
        };

        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));
        return true;
    }

    private static bool WritePublishArtifact(
        string publishYamlPath,
        string packetDirectory,
        string githubBodyPath,
        string executionUnit,
        int? issueNumber,
        string issueUrl)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(publishYamlPath)!);

        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = executionUnit,
            PublishStatus = PublishStatusIssueCreated,
            PacketPath = packetDirectory,
            IssueBodyPath = githubBodyPath,
            CreatedIssueNumber = issueNumber,
            CreatedIssueUrl = issueUrl,
            PublishedLabelName = null,
        };

        File.WriteAllText(publishYamlPath, IssuePublishArtifactYaml.Serialize(artifact));
        return true;
    }

    private static bool AppendIssueCreatedRunEvent(
        string runLogPath,
        string executionUnit,
        string repo,
        int? issueNumber,
        string issueUrl,
        DateTimeOffset publishedAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(runLogPath)!);

        var linkedIssueDescriptor = issueNumber.HasValue
            ? $"{repo}#{issueNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : issueUrl;

        var runEvent = new RunEvent
        {
            Ts = publishedAt,
            ExecutionUnit = executionUnit,
            Event = IssueCreatedEventName,
            By = IssueCreatedEventBy,
            LinkedIssue = linkedIssueDescriptor,
            Reason = issueUrl,
        };

        var line = RunLogSerializer.SerializeLine(runEvent);

        if (File.Exists(runLogPath))
        {
            var existing = File.ReadAllText(runLogPath);
            if (!existing.EndsWith("\n", StringComparison.Ordinal) && existing.Length > 0)
            {
                File.AppendAllText(runLogPath, "\n");
            }
        }

        File.AppendAllText(runLogPath, line + "\n");
        return true;
    }

    private static IssuePublishFlowResult NewResult(
        string executionUnit,
        string domain,
        string repo,
        string packetDirectory,
        string githubBodyPath,
        string publishYamlPath,
        bool write,
        bool packetExists,
        bool githubBodyPresent,
        IReadOnlyList<string> missingSections,
        string? title,
        bool created,
        bool idempotent,
        bool durableStateSynced,
        string? issueUrl,
        int? issueNumber,
        bool queueStatePatched,
        bool publishYamlPatched,
        bool runsAppended,
        string? error,
        string? titleSource = null)
    {
        var nextSteps = new List<string>();
        if (created)
        {
            nextSteps.Add("Commit and push the parent durable state for this execution unit (queue-state, runs, packet files).");
            nextSteps.Add($"Then apply the publish boundary with: intent-cli automation issue-publish --repo {repo} --issue {(issueNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<issue-number>")} --write --format json");
        }
        else if (idempotent)
        {
            nextSteps.Add("Issue already created; durable state is already in sync. No GitHub call was made.");
            if (issueNumber.HasValue)
            {
                nextSteps.Add($"Apply the publish boundary if needed with: intent-cli automation issue-publish --repo {repo} --issue {issueNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} --write --format json");
            }
        }

        return new IssuePublishFlowResult
        {
            ExecutionUnit = executionUnit,
            Domain = domain,
            Repo = repo,
            PacketDirectory = packetDirectory,
            GithubBodyPath = githubBodyPath,
            PublishYamlPath = publishYamlPath,
            PacketExists = packetExists,
            GithubBodyPresent = githubBodyPresent,
            MissingContractSections = missingSections,
            Mode = write ? ModeWrite : ModeDryRun,
            Title = title,
            Created = created,
            Idempotent = idempotent,
            DurableStateSynced = durableStateSynced,
            IssueUrl = issueUrl,
            IssueNumber = issueNumber,
            QueueStatePatched = queueStatePatched,
            PublishYamlPatched = publishYamlPatched,
            RunsAppended = runsAppended,
            IntentTargetApplied = false,
            NextSteps = nextSteps,
            // G290: surface the structured title source so the operator can
            // audit `packet-yaml` vs `github-body-h1` vs `fallback-untitled`.
            // The fallback case adds a `title-fallback` warning so a fallback
            // publish never fails silently.
            TitleSource = titleSource,
            Warnings = string.Equals(titleSource, TitleSourceFallbackUntitled, StringComparison.Ordinal)
                ? new[] { "title-fallback" }
                : Array.Empty<string>(),
            Error = error
        };
    }

    private static void EmitResult(TextWriter writer, IssuePublishFlowResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
    }

    private static void WriteMarkdown(TextWriter writer, IssuePublishFlowResult result)
    {
        writer.WriteLine($"# Issue publish-flow — {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- repo: {result.Repo}");
        writer.WriteLine($"- packet directory: {result.PacketDirectory}");
        writer.WriteLine($"- publish.yaml: {result.PublishYamlPath}");
        writer.WriteLine($"- packet exists: {(result.PacketExists ? "yes" : "no")}");
        writer.WriteLine($"- github-body.md present: {(result.GithubBodyPresent ? "yes" : "no")}");
        writer.WriteLine($"- mode: {result.Mode}");
        if (!string.IsNullOrWhiteSpace(result.Title))
        {
            writer.WriteLine($"- title: {result.Title}");
        }

        if (!string.IsNullOrWhiteSpace(result.TitleSource))
        {
            writer.WriteLine($"- title source: {result.TitleSource}");
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine($"- warnings: {string.Join(", ", result.Warnings)}");
        }

        writer.WriteLine();

        writer.WriteLine("## Contract validation");
        if (result.MissingContractSections.Count == 0)
        {
            writer.WriteLine("- missing sections: none");
        }
        else
        {
            writer.WriteLine("- missing sections:");
            foreach (var section in result.MissingContractSections)
            {
                writer.WriteLine($"  - {section}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Outcome");
        writer.WriteLine($"- created: {(result.Created ? "yes" : "no")}");
        writer.WriteLine($"- idempotent: {(result.Idempotent ? "yes" : "no")}");
        writer.WriteLine($"- durable_state_synced: {(result.DurableStateSynced ? "yes" : "no")}");
        if (result.IssueNumber is { } issueNumber)
        {
            writer.WriteLine($"- issue number: {issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        if (!string.IsNullOrWhiteSpace(result.IssueUrl))
        {
            writer.WriteLine($"- issue URL: {result.IssueUrl}");
        }
        writer.WriteLine($"- queue_state_patched: {(result.QueueStatePatched ? "yes" : "no")}");
        writer.WriteLine($"- publish_yaml_patched: {(result.PublishYamlPatched ? "yes" : "no")}");
        writer.WriteLine($"- runs_appended: {(result.RunsAppended ? "yes" : "no")}");
        writer.WriteLine($"- intent-target applied: {(result.IntentTargetApplied ? "yes" : "no — apply only at the explicit publish boundary after parent durable state is pushed")}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine($"- error: {result.Error}");
        }
        writer.WriteLine();

        if (result.NextSteps.Count > 0)
        {
            writer.WriteLine("## Next steps");
            foreach (var step in result.NextSteps)
            {
                writer.WriteLine($"- {step}");
            }
        }
    }

    private static bool ContainsSectionHeading(string content, string section)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = line.TrimStart('#').Trim();
            if (string.Equals(heading, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// G290: title source constants reported on the publish result so the
    /// operator can audit which path resolved the title.
    /// </summary>
    public const string TitleSourcePacketYaml = "packet-yaml";
    public const string TitleSourceGithubBodyH1 = "github-body-h1";
    public const string TitleSourceFallbackUntitled = "fallback-untitled";

    /// <summary>
    /// G290: resolves the title in priority order: `packet.yaml` `title:` →
    /// body H1 (`# Title`) → fallback `<execution-unit> (untitled)`. Returns
    /// both the title and a structured source string so the caller can
    /// report which path resolved it (and emit a warning when the fallback
    /// fired).
    /// </summary>
    internal static (string Title, string Source) ResolveTitleWithSource(
        string executionUnit,
        string packetDirectory,
        string githubBodyPath)
    {
        // (1) Prefer packet.yaml `title:` when present and non-empty.
        var packetYamlPath = Path.Combine(packetDirectory, "packet.yaml");
        if (File.Exists(packetYamlPath))
        {
            var packetTitle = TryReadPacketTitle(packetYamlPath);
            if (!string.IsNullOrWhiteSpace(packetTitle))
            {
                return (packetTitle!, TitleSourcePacketYaml);
            }
        }

        // (2) Fall back to body H1 for packets that don't carry the title in
        // metadata (older packets, hand-authored bodies).
        if (File.Exists(githubBodyPath))
        {
            var lines = File.ReadAllLines(githubBodyPath);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    return (line[2..].Trim(), TitleSourceGithubBodyH1);
                }

                break;
            }
        }

        // (3) Last-resort deterministic fallback. The caller surfaces this
        // as a warning so the operator can repair packet metadata.
        return ($"{executionUnit} (untitled)", TitleSourceFallbackUntitled);
    }

    /// <summary>
    /// G298: prepend the execution-unit token to the resolved title when the
    /// title does not already start with it. The operator scans the GitHub
    /// issue list by execution unit (G294, SKS-G190, etc.), so a title like
    /// <c>Add host branch policy ...</c> stored without the token would hide
    /// the issue's correlation key. Already-prefixed titles and the
    /// deterministic <c>&lt;id&gt; (untitled)</c> fallback are returned
    /// verbatim — we only insert the prefix when missing. The check is
    /// case-sensitive because execution units are uppercase identifiers.
    /// </summary>
    internal static string FormatIssueTitle(string executionUnit, string? resolvedTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (string.IsNullOrWhiteSpace(resolvedTitle))
        {
            return $"{executionUnit} (untitled)";
        }

        var trimmed = resolvedTitle.Trim();
        if (trimmed.StartsWith(executionUnit + " ", StringComparison.Ordinal)
            || string.Equals(trimmed, executionUnit, StringComparison.Ordinal)
            || trimmed.StartsWith(executionUnit + ":", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return $"{executionUnit} {trimmed}";
    }

    /// <summary>
    /// G290: reads the top-level <c>title:</c> scalar from packet.yaml. The
    /// packet schema places the field at the root (older packets) or under
    /// `implementation_issue_packet:` (Sekiban-style); we accept either by
    /// scanning for the first `title:` line that has a non-empty value, with
    /// optional surrounding quotes stripped.
    /// </summary>
    private static string? TryReadPacketTitle(string packetYamlPath)
    {
        try
        {
            using var reader = new StreamReader(packetYamlPath);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = trimmed["title:".Length..].Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                if (value.Length >= 2
                    && ((value[0] == '"' && value[^1] == '"')
                        || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (IOException)
        {
            // Unreadable packet.yaml — fall through to body H1 / untitled.
        }
        catch (UnauthorizedAccessException)
        {
            // Permission denied — fall through.
        }

        return null;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? repo,
        out string? domainOverride,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = null;
        repo = null;
        domainOverride = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--write":
                    write = true;
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
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown argument '{argument}'.";
                        return false;
                    }

                    if (executionUnit is not null)
                    {
                        error = $"Only one execution-unit id is allowed (got '{executionUnit}' and '{argument}').";
                        return false;
                    }

                    executionUnit = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "An execution-unit id is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("issue publish-flow");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Validates the packet, creates the GitHub issue without intent-target, syncs parent durable state (queue-state, publish.yaml, runs.jsonl), and reports the publish boundary as a next step.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal interface IIssueCreator
{
    IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath);
}

internal sealed record IssueCreateOutcome(string IssueUrl);

internal sealed class GhCliIssueCreator : IIssueCreator
{
    public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("issue");
        startInfo.ArgumentList.Add("create");
        startInfo.ArgumentList.Add("--repo");
        startInfo.ArgumentList.Add(repo);
        startInfo.ArgumentList.Add("--title");
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add("--body-file");
        startInfo.ArgumentList.Add(bodyFilePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gh process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"gh issue create exit {process.ExitCode}: {stderr.Trim()}");
        }

        var url = stdout.Trim().Split('\n').LastOrDefault(line => line.StartsWith("https://", StringComparison.Ordinal))
            ?? stdout.Trim();
        return new IssueCreateOutcome(url);
    }
}

internal sealed record IssuePublishFlowResult
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("packet_directory")]
    public required string PacketDirectory { get; init; }

    [JsonPropertyName("github_body_path")]
    public required string GithubBodyPath { get; init; }

    [JsonPropertyName("publish_yaml_path")]
    public required string PublishYamlPath { get; init; }

    [JsonPropertyName("packet_exists")]
    public required bool PacketExists { get; init; }

    [JsonPropertyName("github_body_present")]
    public required bool GithubBodyPresent { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// G298: explicit alias for <see cref="Title"/> emphasizing that this
    /// is the final title sent to <c>gh issue create</c>. Same value; the
    /// distinct field name lets operators and downstream tooling read
    /// "issue_title" without re-inferring it from <c>title</c> when audit
    /// trails care about the exact GitHub-side string.
    /// </summary>
    [JsonPropertyName("issue_title")]
    public string? IssueTitle => Title;

    /// <summary>
    /// G290: which path resolved <see cref="Title"/>. One of
    /// <see cref="IssuePublishFlowCommand.TitleSourcePacketYaml"/>,
    /// <see cref="IssuePublishFlowCommand.TitleSourceGithubBodyH1"/>, or
    /// <see cref="IssuePublishFlowCommand.TitleSourceFallbackUntitled"/>.
    /// Null for early-exit cases where the title was never resolved.
    /// </summary>
    [JsonPropertyName("title_source")]
    public string? TitleSource { get; init; }

    /// <summary>
    /// G290: structured warnings, e.g. <c>title-fallback</c> when the title
    /// resolved to <c>&lt;execution-unit&gt; (untitled)</c>.
    /// </summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("created")]
    public required bool Created { get; init; }

    [JsonPropertyName("idempotent")]
    public required bool Idempotent { get; init; }

    [JsonPropertyName("durable_state_synced")]
    public required bool DurableStateSynced { get; init; }

    [JsonPropertyName("issue_url")]
    public string? IssueUrl { get; init; }

    [JsonPropertyName("issue_number")]
    public int? IssueNumber { get; init; }

    [JsonPropertyName("queue_state_patched")]
    public required bool QueueStatePatched { get; init; }

    [JsonPropertyName("publish_yaml_patched")]
    public required bool PublishYamlPatched { get; init; }

    [JsonPropertyName("runs_appended")]
    public required bool RunsAppended { get; init; }

    [JsonPropertyName("intent_target_applied")]
    public required bool IntentTargetApplied { get; init; }

    [JsonPropertyName("next_steps")]
    public required IReadOnlyList<string> NextSteps { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
