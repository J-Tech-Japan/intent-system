using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal enum DirectRunEntryKind
{
    Implement,
    Fix,
    Review
}

internal sealed record DirectRunResolvedPolicy
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string Transport { get; init; }

    public required string Command { get; init; }

    public required IReadOnlyList<string> ArgsTemplate { get; init; }
}

internal static class DirectRunCommandSupport
{
    private const string LifecycleEventActor = "intent-cli";
    private const string LifecycleEventName = "provider-lifecycle";

    public static DirectRunLaunchResult CreateAndLaunch(
        CliContext context,
        DirectRunEntryKind entryKind,
        string executionUnit,
        string upstreamRequestRef,
        string workingDirectory,
        IDirectRunLauncher launcher,
        DateTimeOffset launchedAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamRequestRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(launcher);

        var policy = ResolvePolicy(context, entryKind);
        var entryKindValue = FormatEntryKind(entryKind);
        var relativeArtifactPath = ResolveArtifactPath(context, executionUnit);
        var relativeProviderEventLogPath = ResolveProviderEventLogPath(context, executionUnit);
        var absoluteArtifactPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar)));
        var absoluteProviderEventLogPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeProviderEventLogPath.Replace('/', Path.DirectorySeparatorChar)));
        var absoluteUpstreamRequestPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, upstreamRequestRef.Replace('/', Path.DirectorySeparatorChar)));
        var launchResult = launcher.Launch(
            executionUnit,
            entryKindValue,
            relativeArtifactPath,
            relativeProviderEventLogPath,
            policy.Provider,
            policy.Model,
            policy.Transport,
            policy.Command,
            policy.ArgsTemplate,
            launchedAt,
            workingDirectory,
            absoluteUpstreamRequestPath,
            absoluteProviderEventLogPath);

        var artifact = new DirectRunRequestArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = entryKindValue,
            UpstreamRequestRef = upstreamRequestRef,
            Provider = launchResult.Provider,
            Model = launchResult.Model,
            Transport = launchResult.Transport,
            LaunchedAt = launchedAt.ToString("O"),
            ProviderSessionId = launchResult.ProviderSessionId,
            TransportSummary = launchResult.TransportSummary
        };

        var directoryPath = Path.GetDirectoryName(absoluteArtifactPath)
            ?? throw new InvalidOperationException("Direct run request artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absoluteArtifactPath, DirectRunRequestArtifactJson.Serialize(artifact));

        var synthesis = SynthesizeAndPersistResult(
            context,
            executionUnit,
            entryKindValue,
            upstreamRequestRef,
            launchedAt,
            launchResult);

        return launchResult with
        {
            ResultArtifactPath = synthesis.ResultArtifactPath,
            RunStatus = synthesis.RunStatus
        };
    }

    private static DirectRunResolvedPolicy ResolvePolicy(
        CliContext context,
        DirectRunEntryKind entryKind)
    {
        var directRun = context.Config.DirectRun;
        var entryConfig = entryKind switch
        {
            DirectRunEntryKind.Implement => directRun.Implement,
            DirectRunEntryKind.Fix => directRun.Fix,
            DirectRunEntryKind.Review => directRun.Review,
            _ => throw new InvalidOperationException($"Unsupported direct run entry kind '{entryKind}'.")
        };

        var fallbackProvider = entryKind switch
        {
            DirectRunEntryKind.Implement => context.Config.Roles.Implement,
            DirectRunEntryKind.Fix => context.Config.Roles.Implement,
            DirectRunEntryKind.Review => context.Config.Roles.Review,
            _ => throw new InvalidOperationException($"Unsupported direct run entry kind '{entryKind}'.")
        };

        var provider = FirstNonEmpty(entryConfig.Provider, directRun.Provider, fallbackProvider);
        var model = ResolveModel(
            provider,
            FirstNonEmpty(entryConfig.Model, directRun.Model, CliRuntimeContracts.DefaultDirectRunModel));
        var transport = FirstNonEmpty(
            entryConfig.Transport,
            directRun.Transport,
            CliRuntimeContracts.DefaultDirectRunTransport);
        var command = FirstNonEmpty(entryConfig.Command, directRun.Command, ResolveDefaultCommand(provider));
        var argsTemplate = FirstNonEmptyList(
            entryConfig.Args,
            directRun.Args,
            ResolveDefaultArgsTemplate(provider));

        return new DirectRunResolvedPolicy
        {
            Provider = provider,
            Model = model,
            Transport = transport,
            Command = command,
            ArgsTemplate = argsTemplate
        };
    }

    private static string ResolveModel(string provider, string configuredModel)
    {
        if (string.Equals(provider.Trim(), "codex", StringComparison.OrdinalIgnoreCase)
            && string.Equals(configuredModel.Trim(), CliRuntimeContracts.DefaultDirectRunModel, StringComparison.OrdinalIgnoreCase))
        {
            return CliRuntimeContracts.DefaultCodexDirectRunModel;
        }

        return configuredModel;
    }

    private static string ResolveArtifactPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.request.json";
    }

    private static string ResolveProviderEventLogPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.provider.jsonl";
    }

    private static string ResolveResultArtifactPath(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.result.json";
    }

    private static string FormatEntryKind(DirectRunEntryKind entryKind)
    {
        return entryKind switch
        {
            DirectRunEntryKind.Implement => "implement",
            DirectRunEntryKind.Fix => "fix",
            DirectRunEntryKind.Review => "review",
            _ => throw new InvalidOperationException($"Unsupported direct run entry kind '{entryKind}'.")
        };
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException("Direct run policy resolution must produce a non-empty value.");
    }

    private static IReadOnlyList<string> FirstNonEmptyList(params IReadOnlyList<string>[] values)
    {
        foreach (var value in values)
        {
            if (value.Count > 0)
            {
                return value;
            }
        }

        throw new InvalidOperationException("Direct run policy resolution must produce a non-empty args template.");
    }

    private static string ResolveDefaultCommand(string provider)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "codex" => "codex",
            "claude" => "claude",
            _ => provider
        };
    }

    private static IReadOnlyList<string> ResolveDefaultArgsTemplate(string provider)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "codex" => ["exec", "--model", "{model}", "{prompt}"],
            "claude" => ["--print", "--model", "{model}", "--output-format", "json", "{prompt}"],
            _ => ["{prompt}"]
        };
    }

    private static DirectRunSynthesisResult SynthesizeAndPersistResult(
        CliContext context,
        string executionUnit,
        string entryKind,
        string upstreamRequestRef,
        DateTimeOffset launchedAt,
        DirectRunLaunchResult launchResult)
    {
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null)
            ?? throw new InvalidOperationException($"No queue state found at {context.GetQueueStatePath()}");
        var queueItem = queueState.Items.FirstOrDefault(item =>
                            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException(
                            $"Execution unit '{executionUnit}' was not found in queue state for direct run result synthesis.");

        var providerEventLogPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            launchResult.ProviderEventLogPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(providerEventLogPath))
        {
            throw new InvalidOperationException(
                $"Provider raw event log was not found at {providerEventLogPath}");
        }

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        if (providerEvents.Count == 0)
        {
            throw new InvalidOperationException(
                $"Provider raw event log at {providerEventLogPath} did not contain any events.");
        }

        var currentProviderEvents = SelectCurrentSessionEvents(providerEvents, launchResult.ProviderSessionId, launchedAt);

        var runLogPath = context.GetRunLogPath();
        var existingRunEvents = File.Exists(runLogPath)
            ? RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath))
            : [];
        var latestLinkedPr = LatestLinkedPrResolver.TryResolve(existingRunEvents, executionUnit);
        var model = ResolveModel(currentProviderEvents, launchResult.Model);
        var sessionId = ResolveSessionId(currentProviderEvents, launchResult.ProviderSessionId);
        var provider = ResolveProvider(currentProviderEvents, launchResult.Provider);
        var runStatus = ResolveRunStatus(currentProviderEvents);
        var worktreePath = RunStartCommand.ResolveWorktreePath(context, executionUnit);
        var resultArtifactPath = ResolveResultArtifactPath(context, executionUnit);
        var absoluteResultArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            resultArtifactPath.Replace('/', Path.DirectorySeparatorChar)));

        var artifact = new DirectRunResultArtifact
        {
            SchemaVersion = "1",
            ExecutionUnit = executionUnit,
            EntryKind = entryKind,
            UpstreamRequestRef = upstreamRequestRef,
            Provider = provider,
            Model = model,
            SessionId = sessionId,
            RunStatus = runStatus,
            RawLogRef = launchResult.ProviderEventLogPath,
            PacketRef = queueItem.PacketPaths.Yaml,
            ReviewContextRef = queueItem.PacketPaths.ReviewContext,
            LinkedIssue = queueItem.LinkedIssue is null
                ? null
                : new DirectRunLinkedIssueContext
                {
                    Repo = queueItem.LinkedIssue.Repo,
                    Number = queueItem.LinkedIssue.Number,
                    Url = queueItem.LinkedIssue.Url
                },
            LinkedPr = CreateLinkedPullRequestContext(latestLinkedPr),
            Worktree = new DirectRunWorktreeContext
            {
                Path = worktreePath
            }
        };

        var resultDirectoryPath = Path.GetDirectoryName(absoluteResultArtifactPath)
            ?? throw new InvalidOperationException("Direct run result artifact path did not contain a directory.");
        Directory.CreateDirectory(resultDirectoryPath);
        File.WriteAllText(absoluteResultArtifactPath, DirectRunResultArtifactJson.Serialize(artifact));

        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(new RunEvent
            {
                Ts = launchedAt,
                ExecutionUnit = executionUnit,
                Event = LifecycleEventName,
                By = LifecycleEventActor,
                LinkedIssue = artifact.LinkedIssue?.Url,
                LinkedPr = artifact.LinkedPr?.Url,
                EntryKind = entryKind,
                Provider = provider,
                Model = model,
                SessionId = sessionId,
                RunStatus = runStatus,
                RawLogRef = artifact.RawLogRef,
                ResultRef = resultArtifactPath,
                PacketRef = artifact.PacketRef,
                ReviewContextRef = artifact.ReviewContextRef,
                WorktreePath = artifact.Worktree.Path
            }) + Environment.NewLine);

        return new DirectRunSynthesisResult
        {
            ResultArtifactPath = resultArtifactPath,
            RunStatus = runStatus
        };
    }

    private static IReadOnlyList<DirectRunProviderEvent> SelectCurrentSessionEvents(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string launchedSessionId,
        DateTimeOffset launchedAt)
    {
        return DirectRunSessionBoundary.SelectEvents(providerEvents, launchedSessionId, launchedAt);
    }

    private static string ResolveProvider(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string fallbackProvider)
    {
        foreach (var providerEvent in providerEvents)
        {
            if (!string.IsNullOrWhiteSpace(providerEvent.Provider))
            {
                return providerEvent.Provider;
            }
        }

        return fallbackProvider;
    }

    private static string ResolveSessionId(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string fallbackSessionId)
    {
        foreach (var providerEvent in providerEvents)
        {
            if (!string.IsNullOrWhiteSpace(providerEvent.SessionId))
            {
                return providerEvent.SessionId;
            }
        }

        return fallbackSessionId;
    }

    private static string ResolveModel(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string fallbackModel)
    {
        foreach (var providerEvent in providerEvents)
        {
            if (!string.Equals(providerEvent.Kind, "session-metadata", StringComparison.Ordinal))
            {
                continue;
            }

            if (providerEvent.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
                && providerEvent.Payload.TryGetProperty("model", out var modelElement)
                && modelElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var model = modelElement.GetString();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    return model;
                }
            }
        }

        return fallbackModel;
    }

    private static string ResolveRunStatus(IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            var payload = providerEvents[index].Payload;
            var runStatus = TryNormalizeRunStatusFromPayload(payload);
            if (!string.IsNullOrWhiteSpace(runStatus))
            {
                return runStatus;
            }
        }

        return "running";
    }

    private static string? TryNormalizeRunStatusFromPayload(System.Text.Json.JsonElement payload)
    {
        if (payload.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        if (TryReadString(payload, "run_status", out var runStatus))
        {
            return NormalizeRunStatus(runStatus);
        }

        if (TryReadString(payload, "status", out var status))
        {
            return NormalizeRunStatus(status);
        }

        if (TryReadString(payload, "disposition", out var disposition))
        {
            return NormalizeRunStatus(disposition);
        }

        if (TryReadInt32(payload, "exit_code", out var exitCode)
            || TryReadInt32(payload, "exitCode", out exitCode))
        {
            return exitCode == 0 ? "succeeded" : "failed";
        }

        return null;
    }

    private static bool TryReadString(
        System.Text.Json.JsonElement payload,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!payload.TryGetProperty(propertyName, out var element)
            || element.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadInt32(
        System.Text.Json.JsonElement payload,
        string propertyName,
        out int value)
    {
        value = 0;

        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return int.TryParse(
                element.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }

    private static string NormalizeRunStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "success" or "completed" => "succeeded",
            "error" => "failed",
            var normalized when !string.IsNullOrWhiteSpace(normalized) => normalized,
            _ => "running"
        };
    }

    private static DirectRunLinkedPullRequestContext? CreateLinkedPullRequestContext(string? linkedPrUrl)
    {
        if (string.IsNullOrWhiteSpace(linkedPrUrl))
        {
            return null;
        }

        var repo = default(string);
        var number = default(int?);

        if (TryParseGitHubPullRequestUrl(linkedPrUrl, out var parsedRepo, out var parsedNumber))
        {
            repo = parsedRepo;
            number = parsedNumber;
        }

        return new DirectRunLinkedPullRequestContext
        {
            Repo = repo,
            Number = number,
            Url = linkedPrUrl
        };
    }

    private static bool TryParseGitHubPullRequestUrl(
        string pullRequestUrl,
        out string repo,
        out int number)
    {
        repo = string.Empty;
        number = 0;

        if (!Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4
            || !string.Equals(segments[2], "pull", StringComparison.Ordinal)
            || !int.TryParse(segments[3], out number))
        {
            return false;
        }

        repo = $"{segments[0]}/{segments[1]}";
        return true;
    }

    private sealed record DirectRunSynthesisResult
    {
        public required string ResultArtifactPath { get; init; }

        public required string RunStatus { get; init; }
    }
}
