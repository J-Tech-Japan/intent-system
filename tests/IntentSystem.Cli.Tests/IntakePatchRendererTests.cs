using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakePatchRendererTests
{
    [Fact]
    public void RenderMarkdown_GivenPatchRequest_RendersDeterministicArtifact()
    {
        var markdown = IntakePatchRenderer.RenderMarkdown(new IntakePatchRequest
        {
            Domain = "auth",
            TargetFilePaths =
            [
                "intents/intent-cli/concepts/auth-oauth2.md",
                "intents/intent-cli/intent-tree/means/auth-oauth2.md"
            ],
            SourceConceptRefs = ["intents/intent-cli/concepts/auth-oauth2.md"],
            FileDrafts =
            [
                new IntakePatchFileDraft
                {
                    TargetFilePath = "intents/intent-cli/intent-tree/means/auth-oauth2.md",
                    CurrentFileState = "present",
                    ProposedEdits = ["Apply update candidate: Add device-code note"],
                    Rationale = ["This path is listed in return_to_intent_paths."],
                    SourceConceptRefs = ["intents/intent-cli/concepts/auth-oauth2.md"],
                    FoldinAnchors =
                    [
                        "answered_question_ids:iq-1",
                        "recommended_updates:Add device-code note",
                        "return_to_intent_paths:intents/intent-cli/intent-tree/means/auth-oauth2.md"
                    ],
                    CurrentFileExcerpt = "# Existing Heading"
                }
            ]
        });

        Assert.Contains("# Intake Patch Draft", markdown, StringComparison.Ordinal);
        Assert.Contains("target_file_paths:", markdown, StringComparison.Ordinal);
        Assert.Contains("## File-By-File Patch Candidates", markdown, StringComparison.Ordinal);
        Assert.Contains("current_file_state: present", markdown, StringComparison.Ordinal);
        Assert.Contains("foldin_anchors:", markdown, StringComparison.Ordinal);
        Assert.Contains("proposed_edits:", markdown, StringComparison.Ordinal);
        Assert.Contains("rationale:", markdown, StringComparison.Ordinal);
        Assert.Contains("current_file_excerpt:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSummary_GivenPatchRequest_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakePatchRenderer.WriteSummary(
            writer,
            new IntakePatchRequest
            {
                Domain = "auth",
                TargetFilePaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
                SourceConceptRefs = ["intents/intent-cli/concepts/auth-oauth2.md"],
                FileDrafts =
                [
                    new IntakePatchFileDraft
                    {
                        TargetFilePath = "intents/intent-cli/intent-tree/means/auth-oauth2.md",
                        CurrentFileState = "present",
                        ProposedEdits = ["Apply update candidate: Add device-code note"],
                        Rationale = ["This path is listed in return_to_intent_paths."],
                        SourceConceptRefs = ["intents/intent-cli/concepts/auth-oauth2.md"],
                        FoldinAnchors = ["answered_question_ids:iq-1"],
                        CurrentFileExcerpt = "# Existing Heading"
                    }
                ]
            },
            "/repo/.intent-cli/intake/auth.patch.md");

        var output = writer.ToString();
        Assert.Contains("Intake patch draft generated for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Artifact path: /repo/.intent-cli/intake/auth.patch.md", output, StringComparison.Ordinal);
        Assert.Contains("Target file paths: 1", output, StringComparison.Ordinal);
        Assert.Contains("File draft sections: 1", output, StringComparison.Ordinal);
        Assert.Contains("Source concept refs: 1", output, StringComparison.Ordinal);
    }
}
