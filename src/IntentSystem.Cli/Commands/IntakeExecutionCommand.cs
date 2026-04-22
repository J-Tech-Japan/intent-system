namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length > 0 && string.Equals(args[0], "apply", StringComparison.Ordinal))
        {
            return IntakeExecutionApplyCommand.Execute(context, args[1..], writer);
        }

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake execution command requires a domain.");
            return 1;
        }

        var domain = args[0];

        try
        {
            var (request, artifactPath) = ExecuteCore(context.RepoRoot, domain);
            IntakeExecutionRenderer.WriteSummary(writer, request, artifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static (IntakeExecutionRequest Request, string ArtifactPath) ExecuteCore(string repoRoot, string domain)
    {
        var patchPath = Path.Combine(
            repoRoot,
            IntakePatchArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(patchPath))
        {
            throw new InvalidOperationException($"Intake patch artifact was not found at {patchPath}");
        }

        var patchRequest = IntakePatchArtifactMarkdown.Deserialize(File.ReadAllText(patchPath));
        if (!string.Equals(patchRequest.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake patch artifact domain '{patchRequest.Domain}' does not match requested domain '{domain}'.");
        }

        var request = CreateRequest(repoRoot, patchRequest);
        if (request.ProposedExecutionUnits.Count == 0)
        {
            throw new InvalidOperationException($"No updated parent source files were found for domain '{domain}'.");
        }

        var markdown = IntakeExecutionRenderer.RenderMarkdown(request);
        var artifactPath = IntakeExecutionArtifactWriter.Write(markdown, domain, repoRoot);
        return (request, artifactPath);
    }

    private static IntakeExecutionRequest CreateRequest(string repoRoot, IntakePatchRequest patchRequest)
    {
        var updatedSourceFiles = patchRequest.TargetFilePaths
            .Distinct(StringComparer.Ordinal)
            .OrderBy(relativePath => relativePath, StringComparer.Ordinal)
            .Where(relativePath => File.Exists(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        var candidates = updatedSourceFiles.Length == 0
            ? CreateCandidatesFromExecutionSourceOfTruth(repoRoot, patchRequest.Domain)
            : updatedSourceFiles
                .Select((relativePath, index) => CreateCandidate(repoRoot, patchRequest.Domain, relativePath, index + 1))
                .ToArray();

        var conceptIds = candidates
            .Where(candidate => string.Equals(candidate.TargetPart, "concepts", StringComparison.Ordinal))
            .Select(candidate => candidate.ExecutionUnitId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var resolvedCandidates = candidates
            .Select(candidate => candidate with
            {
                Dependencies = string.Equals(candidate.TargetPart, "concepts", StringComparison.Ordinal)
                    ? Array.Empty<string>()
                    : conceptIds.Length == 0
                        ? candidate.Dependencies
                        : conceptIds
            })
            .ToArray();

        return new IntakeExecutionRequest
        {
            Domain = patchRequest.Domain,
            ProposedExecutionUnits = resolvedCandidates
        };
    }

    private static IntakeExecutionUnitCandidate[] CreateCandidatesFromExecutionSourceOfTruth(string repoRoot, string domain)
    {
        var intentsRoot = Path.Combine(repoRoot, "intents");
        if (!Directory.Exists(intentsRoot))
        {
            return [];
        }

        var candidates = new List<IntakeExecutionUnitCandidate>();
        foreach (var absolutePath in Directory.GetFiles(intentsRoot, "*.md", SearchOption.AllDirectories)
                     .Where(path => path.Contains($"{Path.DirectorySeparatorChar}execution{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
            if (!BelongsToDomain(relativePath, domain))
            {
                continue;
            }

            candidates.AddRange(ParseIssueReadyExecutionRows(relativePath, File.ReadAllText(absolutePath)));
        }

        return candidates
            .Where(candidate => HasDomainExecutionUnitPrefix(candidate.ExecutionUnitId, domain))
            .GroupBy(candidate => candidate.ExecutionUnitId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.ExecutionUnitId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool BelongsToDomain(string relativePath, string domain)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith($"intents/{domain}/", StringComparison.Ordinal);
    }

    private static bool HasDomainExecutionUnitPrefix(string executionUnitId, string domain)
    {
        var normalizedDomain = domain.Replace('/', '-').ToUpperInvariant();
        return executionUnitId.StartsWith($"{normalizedDomain}-", StringComparison.Ordinal);
    }

    private static IReadOnlyList<IntakeExecutionUnitCandidate> ParseIssueReadyExecutionRows(string relativePath, string markdown)
    {
        var rows = new List<IntakeExecutionUnitCandidate>();
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var inTargetTable = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("| subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |", StringComparison.Ordinal))
            {
                inTargetTable = true;
                continue;
            }

            if (!inTargetTable)
            {
                continue;
            }

            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                inTargetTable = false;
                continue;
            }

            if (line.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length < 8)
            {
                continue;
            }

            if (!string.Equals(cells[7], "yes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var executionUnitId = cells[0];
            var goal = cells[2];
            var targetPart = cells[6];
            var dependencies = ParseDependencies(cells[3]);

            rows.Add(new IntakeExecutionUnitCandidate
            {
                ExecutionUnitId = executionUnitId,
                SourceFilePath = relativePath,
                TargetPart = targetPart,
                Dependencies = dependencies,
                ReadinessNotes =
                [
                    $"Source file path: {relativePath}",
                    $"Current goal: {goal}",
                    $"Execution row target part: {targetPart}"
                ],
                VerificationHints =
                [
                    $"Review execution row '{executionUnitId}' in '{relativePath}'.",
                    $"Confirm target part '{targetPart}' remains issue-ready in current source-of-truth.",
                    "dotnet test IntentSystem.sln"
                ]
            });
        }

        return rows;
    }

    private static IReadOnlyList<string> ParseDependencies(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)
            || string.Equals(rawValue, "none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(value => !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IntakeExecutionUnitCandidate CreateCandidate(
        string repoRoot,
        string domain,
        string relativePath,
        int ordinal)
    {
        var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(absolutePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.None);
        var heading = lines.FirstOrDefault(line => line.StartsWith("#", StringComparison.Ordinal))?.Trim();
        var bulletCount = lines.Count(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal));
        var targetPart = ResolveTargetPart(relativePath);
        var executionUnitId = $"{domain.ToUpperInvariant()}-{ordinal:00}";

        var readinessNotes = new List<string>
        {
            $"Source file path: {relativePath}",
            $"Current heading: {heading ?? "[none]"}",
            $"Detected bullet lines: {bulletCount}"
        };

        var verificationHints = new List<string>
        {
            $"Review parent source file '{relativePath}' for issue-ready scope.",
            $"Confirm target part '{targetPart}' in the execution draft.",
            "dotnet test IntentSystem.sln"
        };

        return new IntakeExecutionUnitCandidate
        {
            ExecutionUnitId = executionUnitId,
            SourceFilePath = relativePath,
            TargetPart = targetPart,
            Dependencies = [],
            ReadinessNotes = readinessNotes,
            VerificationHints = verificationHints
        };
    }

    private static string ResolveTargetPart(string relativePath)
    {
        const string intentCliPrefix = "intents/intent-cli/";
        var trimmed = relativePath.StartsWith(intentCliPrefix, StringComparison.Ordinal)
            ? relativePath[intentCliPrefix.Length..]
            : relativePath;

        if (trimmed.StartsWith("concepts/", StringComparison.Ordinal))
        {
            return "concepts";
        }

        var lastSlashIndex = trimmed.LastIndexOf('/');
        return lastSlashIndex > 0
            ? trimmed[..lastSlashIndex]
            : trimmed;
    }
}
