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

        return launchResult;
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
        var model = FirstNonEmpty(entryConfig.Model, directRun.Model, CliRuntimeContracts.DefaultDirectRunModel);
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
}
