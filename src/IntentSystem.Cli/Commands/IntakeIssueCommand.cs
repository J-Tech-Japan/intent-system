using System.Globalization;
using IntentSystem.Projection;
using IntentSystem.Projection.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeIssueCommand
{
    private const string IntakeIssueMarker = "`intake issue <domain>`";
    private const string IssueBaselineExecutionUnit = "G37";
    private const string ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md";
    private const string ClarificationReturnPath = "intents/intent-cli/clarifications/open.md";

    private static readonly string[] RulesAndSpecs =
    [
        "intents/intent-cli/specs/04-concept-intake-and-interview.md",
        "intents/intent-cli/specs/05-intent-cli-surface.md",
        "intents/rules/feature-request-intake-and-autostart.md",
        "intents/rules/issue-compilation-and-execution.md",
        "intents/rules/issue-projection-format.md"
    ];

    private static readonly string[] TechnicalBaseline =
    [
        "C# / .NET",
        ".NET 10.0.100+ baseline",
        "dnx / dotnet tool exec",
        "do not switch to Node or TypeScript toolchain",
        "do not commit node_modules or vendor artifacts"
    ];

    private static readonly string[] ProjectLocalGuide =
    [
        "AGENTS.md",
        "CLAUDE.md"
    ];

    private static readonly string[] VerificationEvidence =
    [
        "contract-reviewed",
        "tests-passing",
        "acceptance-criteria-checked"
    ];

    private static readonly string[] OutOfScope =
    [
        "queue mutation",
        "child issue creation",
        "autostart",
        "review execution",
        "merge / closeout",
        "workflow execution"
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake issue command requires a domain.");
            return 1;
        }

        var domain = args[0].Trim();

        try
        {
            var result = ExecuteCore(context.RepoRoot, domain);
            IntakeIssueRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntakeIssueResult ExecuteCore(string repoRoot, string domain)
    {
        var baseline = LoadIssueBaseline(repoRoot);
        var units = LoadExecutionUnits(repoRoot, domain);
        if (units.Count == 0)
        {
            throw new InvalidOperationException(
                $"No intake-origin issue-ready execution units were found for domain '{domain}'.");
        }

        return GenerateArtifacts(repoRoot, domain, units, baseline);
    }

    private static IReadOnlyList<IntakeOriginExecutionUnit> LoadExecutionUnits(string repoRoot, string domain)
    {
        var executionArtifactPath = Path.Combine(
            repoRoot,
            IntakeExecutionArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(executionArtifactPath))
        {
            throw new InvalidOperationException($"Intake execution artifact was not found at {executionArtifactPath}");
        }

        var request = IntakeExecutionArtifactMarkdown.Deserialize(File.ReadAllText(executionArtifactPath));
        if (!string.Equals(request.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake execution artifact domain '{request.Domain}' does not match requested domain '{domain}'.");
        }

        var units = new Dictionary<string, IntakeOriginExecutionUnit>(StringComparer.Ordinal);
        foreach (var candidate in request.ProposedExecutionUnits)
        {
            var sourceAbsolutePath = Path.Combine(repoRoot, candidate.SourceFilePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourceAbsolutePath))
            {
                throw new InvalidOperationException(
                    $"Intake execution unit '{candidate.ExecutionUnitId}' source file was not found at {sourceAbsolutePath}");
            }

            var unit = new IntakeOriginExecutionUnit
            {
                ExecutionUnitId = candidate.ExecutionUnitId,
                SourceFilePath = candidate.SourceFilePath,
                TargetPart = candidate.TargetPart,
                Dependencies = candidate.Dependencies,
                ReadinessNotes = candidate.ReadinessNotes,
                VerificationHints = candidate.VerificationHints
            };

            if (!units.TryAdd(unit.ExecutionUnitId, unit))
            {
                throw new InvalidOperationException(
                    $"Execution unit '{unit.ExecutionUnitId}' was defined multiple times in the intake execution artifact.");
            }
        }

        return units.Values
            .OrderBy(unit => unit.ExecutionUnitId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IntakeIssueResult GenerateArtifacts(
        string repoRoot,
        string domain,
        IReadOnlyList<IntakeOriginExecutionUnit> units,
        IntakeIssueBaseline baseline)
    {
        var generatedExecutionUnits = new List<string>();
        var artifactPaths = new List<string>();
        var skippedUnits = new List<string>();

        foreach (var unit in units.OrderBy(item => item.ExecutionUnitId, StringComparer.Ordinal))
        {
            var packet = PacketGenerator.Generate(CreateRow(unit, baseline), CreateContext(unit, baseline));
            var githubBodyPath = QueueDispatchCommand.ResolveGitHubBodyPath(repoRoot, packet.Paths.Yaml);
            var githubBodyRelativePath = Path.GetRelativePath(repoRoot, githubBodyPath).Replace(Path.DirectorySeparatorChar, '/');

            if (AnyArtifactExists(repoRoot, packet.Paths, githubBodyRelativePath))
            {
                skippedUnits.Add(unit.ExecutionUnitId);
                continue;
            }

            ProjectionArtifactWriter.Write(packet, repoRoot, overwrite: false);

            var githubBodyDirectory = Path.GetDirectoryName(githubBodyPath)
                ?? throw new InvalidOperationException("GitHub body path did not contain a directory.");
            Directory.CreateDirectory(githubBodyDirectory);
            File.WriteAllText(githubBodyPath, packet.ImplementationMarkdown);

            generatedExecutionUnits.Add(unit.ExecutionUnitId);
            artifactPaths.Add(packet.Paths.Implementation);
            artifactPaths.Add(packet.Paths.ReviewContext);
            artifactPaths.Add(packet.Paths.Yaml);
            artifactPaths.Add(githubBodyRelativePath);
        }

        return new IntakeIssueResult
        {
            Domain = domain,
            GeneratedExecutionUnits = generatedExecutionUnits,
            ArtifactPaths = artifactPaths,
            SkippedUnits = skippedUnits
        };
    }

    private static bool AnyArtifactExists(string repoRoot, ResolvedPacketPaths packetPaths, string githubBodyPath)
    {
        return File.Exists(Path.Combine(repoRoot, packetPaths.Implementation.Replace('/', Path.DirectorySeparatorChar)))
            || File.Exists(Path.Combine(repoRoot, packetPaths.ReviewContext.Replace('/', Path.DirectorySeparatorChar)))
            || File.Exists(Path.Combine(repoRoot, packetPaths.Yaml.Replace('/', Path.DirectorySeparatorChar)))
            || File.Exists(Path.Combine(repoRoot, githubBodyPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static IntakeIssueBaseline LoadIssueBaseline(string repoRoot)
    {
        foreach (var relativePath in EnumerateExecutionFiles(repoRoot))
        {
            var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var content = File.ReadAllText(absolutePath);
            if (!content.Contains(IntakeIssueMarker, StringComparison.Ordinal))
            {
                continue;
            }

            var baselineLines = ExtractSectionBulletLines(content, IntakeIssueMarker);
            var row = ParseIssueBaselineRow(content);

            return new IntakeIssueBaseline
            {
                ExecutionFilePath = relativePath,
                TargetRepo = row.TargetRepo,
                TargetPath = row.TargetPath,
                IntentBaseline = baselineLines
            };
        }

        throw new InvalidOperationException(
            "Intake issue baseline could not be derived from the current execution source-of-truth.");
    }

    private static IReadOnlyList<string> EnumerateExecutionFiles(string repoRoot)
    {
        var intentsRoot = Path.Combine(repoRoot, "intents");
        if (!Directory.Exists(intentsRoot))
        {
            return [];
        }

        return Directory.GetFiles(intentsRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}execution{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractSectionBulletLines(string markdown, string marker)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.None);
        var sectionStart = FindSectionStart(lines, marker);
        if (sectionStart < 0)
        {
            return [];
        }

        var sectionEnd = FindSectionEnd(lines, sectionStart);
        var values = new List<string>();
        for (var index = sectionStart + 1; index < sectionEnd; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(trimmed[2..]);
        }

        return values;
    }

    private static int FindSectionStart(IReadOnlyList<string> lines, string marker)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith("## ", StringComparison.Ordinal))
            {
                continue;
            }

            var sectionEnd = FindSectionEnd(lines, index);
            var sectionContent = string.Join("\n", lines.Skip(index).Take(sectionEnd - index));
            if (sectionContent.Contains(marker, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindSectionEnd(IReadOnlyList<string> lines, int sectionStart)
    {
        for (var index = sectionStart + 1; index < lines.Count; index++)
        {
            if (lines[index].StartsWith("## ", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static IssueBaselineRow ParseIssueBaselineRow(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.None);
        var row = lines.FirstOrDefault(line => line.StartsWith($"| {IssueBaselineExecutionUnit} |", StringComparison.Ordinal));
        if (row is null)
        {
            throw new InvalidOperationException(
                "Intake issue baseline row could not be derived from the current execution source-of-truth.");
        }

        var cells = row.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (cells.Length < 8)
        {
            throw new InvalidOperationException(
                "Intake issue baseline row did not contain the expected projection fields.");
        }

        return new IssueBaselineRow
        {
            TargetRepo = cells[4],
            TargetPath = cells[5]
        };
    }

    private static SubSliceRow CreateRow(IntakeOriginExecutionUnit unit, IntakeIssueBaseline baseline)
    {
        return new SubSliceRow
        {
            SourceExecutionUnit = unit.ExecutionUnitId,
            Goal = $"Reflect the intake-origin update from `{unit.SourceFilePath}` into `{unit.TargetPart}`.",
            TargetRepo = baseline.TargetRepo,
            TargetPath = baseline.TargetPath,
            TargetPart = unit.TargetPart,
            DependsOnSubslices = unit.Dependencies,
            RelatedIntents =
            [
                ParentIntentRoot,
                baseline.ExecutionFilePath
            ],
            SourceConcepts =
            [
                unit.SourceFilePath,
                .. RulesAndSpecs
            ],
            SuccessSignal = $"Updated intake-origin source `{unit.SourceFilePath}` is reflected within `{unit.TargetPart}`.",
            ReviewMode = "deterministic-review",
            CompletionAction = "wait-for-deterministic-review",
            LandingPolicy = "merge-after-review"
        };
    }

    private static ProjectionContext CreateContext(IntakeOriginExecutionUnit unit, IntakeIssueBaseline baseline)
    {
        var title = ResolveTitle(unit);

        return new ProjectionContext
        {
            IssueTitle = $"[{unit.ExecutionUnitId}] {title}",
            IssueKind = IssueKind.Feature,
            ParentIntentRoot = ParentIntentRoot,
            ClarificationReturnPath = ClarificationReturnPath,
            AcceptanceCriteria =
            [
                $"Updated intake-origin source `{unit.SourceFilePath}` is reflected within `{unit.TargetPart}`.",
                $"Changes stay limited to target part `{unit.TargetPart}`.",
                $"Work remains scoped to execution unit `{unit.ExecutionUnitId}`."
            ],
            DeterministicReviewChecks = BuildDeterministicReviewChecks(unit),
            VerificationEvidence = VerificationEvidence,
            TechnicalBaseline = TechnicalBaseline,
            ProjectLocalGuide = ProjectLocalGuide,
            IntentBaseline = baseline.IntentBaseline
                .Concat(unit.ReadinessNotes)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            AdditionalInScope =
            [
                $"updated intake-origin source `{unit.SourceFilePath}`",
                $"execution unit `{unit.ExecutionUnitId}`"
            ],
            OutOfScope = OutOfScope
        };
    }

    private static IReadOnlyList<string> BuildDeterministicReviewChecks(IntakeOriginExecutionUnit unit)
    {
        var checks = new List<string>
        {
            $"implementation reflects `{unit.SourceFilePath}` within `{unit.TargetPart}`",
            $"changes stay limited to execution unit `{unit.ExecutionUnitId}`"
        };

        if (unit.Dependencies.Count > 0)
        {
            checks.Add($"dependencies `{string.Join(", ", unit.Dependencies)}` remain satisfied");
        }
        else
        {
            checks.Add("no additional intake-origin dependency drift is introduced");
        }

        return checks;
    }

    private static string ResolveTitle(IntakeOriginExecutionUnit unit)
    {
        var heading = unit.ReadinessNotes
            .FirstOrDefault(note => note.StartsWith("Current heading: ", StringComparison.Ordinal));

        if (heading is not null)
        {
            return heading["Current heading: ".Length..].Trim().TrimStart('#', ' ');
        }

        var fileName = Path.GetFileNameWithoutExtension(unit.SourceFilePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return unit.TargetPart;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(fileName.Replace('-', ' '));
    }

    private readonly record struct IntakeIssueBaseline
    {
        public required string ExecutionFilePath { get; init; }

        public required string TargetRepo { get; init; }

        public required string TargetPath { get; init; }

        public required IReadOnlyList<string> IntentBaseline { get; init; }
    }

    private readonly record struct IssueBaselineRow
    {
        public required string TargetRepo { get; init; }

        public required string TargetPath { get; init; }
    }

    private sealed record IntakeOriginExecutionUnit
    {
        public required string ExecutionUnitId { get; init; }

        public required string SourceFilePath { get; init; }

        public required string TargetPart { get; init; }

        public required IReadOnlyList<string> Dependencies { get; init; }

        public required IReadOnlyList<string> ReadinessNotes { get; init; }

        public required IReadOnlyList<string> VerificationHints { get; init; }
    }
}
