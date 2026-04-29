namespace IntentSystem.Cli.Commands;

/// <summary>
/// Parser for the owner-filled decision artifact consumed by
/// <c>intent-cli clarify record</c> (G182). The expected shape is a Markdown
/// document with at least these sections:
///
/// <code>
/// ## Question
/// &lt;text&gt;
///
/// ## Decision
/// &lt;text&gt;
///
/// ## Rationale  (optional)
/// &lt;text&gt;
/// </code>
///
/// Section bodies span until the next <c>##</c> heading or end of file. Heading
/// titles are matched case-insensitively, but the parser does NOT invent or
/// rewrite content — it only extracts the owner's prose verbatim.
/// </summary>
internal static class ClarifyRecordDecisionParser
{
    public static bool TryParse(string content, out ClarifyRecordDecision? decision, out string error)
    {
        decision = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "decision artifact was empty.";
            return false;
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? question = null;
        string? decisionText = null;
        string? rationale = null;

        var currentHeading = (string?)null;
        var currentBody = new List<string>();

        void Flush()
        {
            if (currentHeading is null)
            {
                return;
            }

            var body = string.Join("\n", currentBody).Trim();
            switch (currentHeading)
            {
                case "question":
                    question = body;
                    break;
                case "decision":
                    decisionText = body;
                    break;
                case "rationale":
                    rationale = body.Length == 0 ? null : body;
                    break;
            }
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimEnd();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                currentBody.Clear();
                currentHeading = trimmed[3..].Trim().ToLowerInvariant();
                continue;
            }

            if (currentHeading is not null)
            {
                currentBody.Add(rawLine);
            }
        }

        Flush();

        if (string.IsNullOrWhiteSpace(question))
        {
            error = "decision artifact is missing a non-empty '## Question' section.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(decisionText))
        {
            error = "decision artifact is missing a non-empty '## Decision' section.";
            return false;
        }

        decision = new ClarifyRecordDecision
        {
            Question = question.Trim(),
            Decision = decisionText.Trim(),
            Rationale = rationale
        };

        return true;
    }
}
