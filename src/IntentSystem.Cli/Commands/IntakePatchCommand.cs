namespace IntentSystem.Cli.Commands;

internal static class IntakePatchCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake patch command requires a domain.");
            return 1;
        }

        var domain = args[0];

        try
        {
            var (request, artifactPath) = ExecuteCore(context.RepoRoot, domain);
            IntakePatchRenderer.WriteSummary(writer, request, artifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static (IntakePatchRequest Request, string ArtifactPath) ExecuteCore(string repoRoot, string domain)
    {
        var foldinPath = Path.Combine(
            repoRoot,
            IntakeFoldinArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(foldinPath))
        {
            throw new InvalidOperationException($"Intake fold-in artifact was not found at {foldinPath}");
        }

        var foldinDraft = IntakeFoldinArtifactMarkdown.Deserialize(File.ReadAllText(foldinPath));
        if (!string.Equals(foldinDraft.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake fold-in artifact domain '{foldinDraft.Domain}' does not match requested domain '{domain}'.");
        }

        var request = CreateRequest(repoRoot, foldinDraft);
        var markdown = IntakePatchRenderer.RenderMarkdown(request);
        var artifactPath = IntakePatchArtifactWriter.Write(markdown, domain, repoRoot);
        return (request, artifactPath);
    }

    private static IntakePatchRequest CreateRequest(string repoRoot, IntakePatchFoldinDraft foldinDraft)
    {
        var targetFilePaths = foldinDraft.ReturnToIntentPaths
            .Concat(foldinDraft.SourceConceptRefs)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var fileDrafts = targetFilePaths
            .Select(targetFilePath => CreateFileDraft(repoRoot, foldinDraft, targetFilePath))
            .ToArray();

        return new IntakePatchRequest
        {
            Domain = foldinDraft.Domain,
            TargetFilePaths = targetFilePaths,
            SourceConceptRefs = foldinDraft.SourceConceptRefs
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray(),
            FileDrafts = fileDrafts
        };
    }

    private static IntakePatchFileDraft CreateFileDraft(
        string repoRoot,
        IntakePatchFoldinDraft foldinDraft,
        string targetFilePath)
    {
        var isReturnTarget = foldinDraft.ReturnToIntentPaths.Contains(targetFilePath, StringComparer.Ordinal);
        var isSourceConceptTarget = foldinDraft.SourceConceptRefs.Contains(targetFilePath, StringComparer.Ordinal);
        var absolutePath = Path.Combine(repoRoot, targetFilePath.Replace('/', Path.DirectorySeparatorChar));
        var fileExists = File.Exists(absolutePath);
        var currentFileState = fileExists ? "present" : "missing";
        var currentFileExcerpt = fileExists
            ? CreateExcerpt(File.ReadAllText(absolutePath))
            : "[missing]";

        var proposedEdits = new List<string>();
        if (isReturnTarget)
        {
            if (foldinDraft.RecommendedUpdates.Count == 0)
            {
                proposedEdits.Add("Align this target file with the current fold-in draft coverage.");
            }
            else
            {
                proposedEdits.AddRange(
                    foldinDraft.RecommendedUpdates
                        .OrderBy(update => update, StringComparer.Ordinal)
                        .Select(update => $"Apply update candidate: {update}"));
            }
        }

        if (isSourceConceptTarget)
        {
            proposedEdits.Add("Reconcile this source concept file with the current fold-in draft.");
        }

        if (!fileExists)
        {
            proposedEdits.Add("Draft this target file as a new parent source-of-truth entry if creation is intended.");
        }

        var rationale = new List<string>();
        if (isReturnTarget)
        {
            rationale.Add("This path is listed in return_to_intent_paths.");
        }

        if (isSourceConceptTarget)
        {
            rationale.Add("This path is listed in source_concept_refs.");
        }

        rationale.Add($"Answered question ids informing this draft: {FormatList(foldinDraft.AnsweredQuestionIds)}.");
        rationale.Add("This patch draft uses current parent source file context only as read-only input.");

        var sourceConceptRefs = (isSourceConceptTarget
                ? [targetFilePath]
                : foldinDraft.SourceConceptRefs.ToArray())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var foldinAnchors = foldinDraft.AnsweredQuestionIds
            .Select(questionId => $"answered_question_ids:{questionId}")
            .Concat(foldinDraft.RecommendedUpdates.OrderBy(update => update, StringComparer.Ordinal)
                .Select(update => $"recommended_updates:{update}"))
            .Concat(isReturnTarget ? [$"return_to_intent_paths:{targetFilePath}"] : [])
            .Concat(isSourceConceptTarget ? [$"source_concept_refs:{targetFilePath}"] : [])
            .ToArray();

        return new IntakePatchFileDraft
        {
            TargetFilePath = targetFilePath,
            CurrentFileState = currentFileState,
            ProposedEdits = proposedEdits.Distinct(StringComparer.Ordinal).ToArray(),
            Rationale = rationale,
            SourceConceptRefs = sourceConceptRefs,
            FoldinAnchors = foldinAnchors,
            CurrentFileExcerpt = currentFileExcerpt
        };
    }

    private static string CreateExcerpt(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var excerptLines = lines.Take(6).ToArray();
        var excerpt = string.Join(Environment.NewLine, excerptLines).TrimEnd();
        return string.IsNullOrEmpty(excerpt) ? "[empty]" : excerpt;
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "none"
            : string.Join(", ", values.OrderBy(value => value, StringComparer.Ordinal));
    }
}
