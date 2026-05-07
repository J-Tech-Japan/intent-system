namespace IntentSystem.Cli.Commands;

/// <summary>
/// Shared structural parser for parent-host
/// <c>intents/&lt;domain&gt;/clarifications/open.md</c> files. The host shape places
/// live blockers as list items under a <c>## Current Open Blockers</c> heading,
/// with an explicit no-blocker sentinel list item when nothing is currently
/// blocking. Detect open blockers structurally so durable prose / recently-resolved
/// notes do not falsely look like an open blocker (G179 review fix; reused by
/// G180 context collect).
///
/// G275: Also recognises two cleared states:
/// - YAML front-matter containing <c>intent_state: clarified</c>
/// - A "Current Open Blockers" section whose only content is the literal text
///   <c>None</c> (case-insensitive) or the established no-blocker sentinel.
///
/// G285: <see cref="Analyze"/> exposes a structured analysis so callers
/// (notably <c>intent next-slice --dry-run</c>) can distinguish a true Hard
/// Clarification (substantive blockers/questions) from stale clarification
/// metadata (front-matter still <c>intent_state: open</c> but body explicitly
/// records "no blockers" / "no open questions"). Bullet items consisting only
/// of the literal word <c>None</c>, or starting with English no-blocker /
/// no-questions phrasing, are now also recognised as no-blocker sentinels, and
/// the existing logic is extended to a parallel <c>## Open Questions</c>
/// section.
/// </summary>
internal static class ClarificationOpenDetector
{
    public static bool HasOpenBlocker(string? content) => Analyze(content).HasOpenBlocker;

    /// <summary>
    /// Returns a structured view of clarification state covering both the
    /// boolean blocker decision and the diagnostic flags needed to detect
    /// stale clarification metadata.
    /// </summary>
    public static ClarificationStateAnalysis Analyze(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ClarificationStateAnalysis
            {
                HasOpenBlocker = false,
                FrontMatterState = null,
                BodyHasNoBlockerSignal = false,
                BodyHasNoOpenQuestionsSignal = false
            };
        }

        var frontMatterState = ExtractFrontMatterState(content!);
        var blockers = ScanSection(content!, "Current Open Blockers");
        var questions = ScanSection(content!, "Open Questions");

        // G275: an explicit `intent_state: clarified` in front-matter overrides
        // body content for the boolean decision so historical bullets do not
        // resurrect a stale blocker.
        var hasOpenBlocker = string.Equals(frontMatterState, "clarified", StringComparison.OrdinalIgnoreCase)
            ? false
            : (blockers.HasSubstantive || questions.HasSubstantive);

        return new ClarificationStateAnalysis
        {
            HasOpenBlocker = hasOpenBlocker,
            FrontMatterState = frontMatterState,
            BodyHasNoBlockerSignal = blockers.HasNoneSignal && !blockers.HasSubstantive,
            BodyHasNoOpenQuestionsSignal = questions.HasNoneSignal && !questions.HasSubstantive
        };
    }

    private static SectionScan ScanSection(string content, string sectionName)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inSection = false;
        var sectionSeen = false;
        var hasSubstantive = false;
        var hasNoneSignal = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                inSection = string.Equals(heading, sectionName, StringComparison.OrdinalIgnoreCase);
                if (inSection)
                {
                    sectionSeen = true;
                }

                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var trimmed = line.Trim();

            // A bare "None" line under the section is the canonical English
            // no-blocker / no-questions signal.
            if (string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
            {
                hasNoneSignal = true;
                continue;
            }

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal)
                && !trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                continue;
            }

            var item = trimmed[2..].Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (IsNoBlockerSentinel(item))
            {
                hasNoneSignal = true;
                continue;
            }

            hasSubstantive = true;
        }

        return new SectionScan(sectionSeen, hasSubstantive, hasNoneSignal);
    }

    private static bool IsNoBlockerSentinel(string item)
    {
        var trimmed = item.Trim();

        // G285: a bullet whose sole content is the literal word "None" is a
        // no-blocker / no-questions sentinel.
        if (string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // G285: parallel English no-blocker / no-questions phrasings used by
        // hosts that prefer explicit sentences over the bare "None" form.
        if (StartsWithNoBlockerPhrase(trimmed))
        {
            return true;
        }

        // Original Japanese sentinel established by host conventions.
        const string japaneseSentinel = "現時点で child issue cut を要する root blocker はない";
        if (trimmed.Contains(japaneseSentinel, StringComparison.Ordinal))
        {
            return true;
        }

        // Defensive English fallback for any future host that uses a parallel
        // English wording. Kept narrow on purpose so unrelated bullets do not
        // accidentally suppress real blockers.
        return trimmed.Contains(
            "no root blocker requiring child issue cut",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithNoBlockerPhrase(string item)
    {
        // Match short, narrowly-scoped sentinel openings only. We deliberately
        // anchor on "no" + a controlled noun ("blockers", "open blockers",
        // "open questions", "current open blockers", etc.) so that bullets such
        // as "Need decision on storage strategy" do not collapse to a sentinel.
        ReadOnlySpan<string> openings =
        [
            "no open blockers",
            "no current open blockers",
            "no blockers",
            "no open questions",
            "no current open questions",
            "no questions",
            "no current blockers",
            "no current questions"
        ];

        foreach (var opening in openings)
        {
            if (item.StartsWith(opening, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the lowercase value of the YAML <c>intent_state</c> field at the
    /// top of the file (either inside a fenced <c>---</c> front-matter block
    /// or as a bare top-level field). Returns null when the field is absent.
    /// </summary>
    private static string? ExtractFrontMatterState(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inFrontMatter = false;
        var frontMatterClosed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            if (i == 0 && string.Equals(line, "---", StringComparison.Ordinal))
            {
                inFrontMatter = true;
                continue;
            }

            if (inFrontMatter)
            {
                if (string.Equals(line, "---", StringComparison.Ordinal)
                    || string.Equals(line, "...", StringComparison.Ordinal))
                {
                    frontMatterClosed = true;
                    break;
                }

                var stateInBlock = ExtractIntentStateValue(line);
                if (stateInBlock is not null)
                {
                    return stateInBlock;
                }

                continue;
            }

            if (!frontMatterClosed && !line.StartsWith("#", StringComparison.Ordinal))
            {
                var stateInline = ExtractIntentStateValue(line);
                if (stateInline is not null)
                {
                    return stateInline;
                }
            }

            if (line.StartsWith("# ", StringComparison.Ordinal)
                || line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }
        }

        return null;
    }

    private static string? ExtractIntentStateValue(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("intent_state:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = trimmed["intent_state:".Length..].Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1];
        }

        return value.ToLowerInvariant();
    }

    private readonly record struct SectionScan(bool Seen, bool HasSubstantive, bool HasNoneSignal);
}

/// <summary>
/// Structured view of a clarification file's state, covering both the boolean
/// "is anything blocking" decision and the diagnostic flags needed to detect
/// stale clarification metadata (front-matter still <c>intent_state: open</c>
/// even though the body explicitly records no current blockers / questions).
/// </summary>
internal sealed record ClarificationStateAnalysis
{
    /// <summary>
    /// True when the file describes a substantive open blocker or open
    /// question that should stop next-slice publish. False when the file is
    /// either explicitly cleared (front-matter or body sentinel) or when the
    /// scanned sections contain no substantive content.
    /// </summary>
    public required bool HasOpenBlocker { get; init; }

    /// <summary>
    /// Lowercase value of the front-matter <c>intent_state:</c> field, or
    /// <c>null</c> when the field is absent.
    /// </summary>
    public required string? FrontMatterState { get; init; }

    /// <summary>
    /// True when the body's <c>## Current Open Blockers</c> section exists and
    /// contains an explicit no-blocker signal (bare "None" line, "- None"
    /// bullet, English "no blockers" phrase, or the Japanese sentinel) without
    /// any substantive bullet.
    /// </summary>
    public required bool BodyHasNoBlockerSignal { get; init; }

    /// <summary>
    /// True when the body's <c>## Open Questions</c> section exists and
    /// contains only a no-questions signal without substantive content.
    /// </summary>
    public required bool BodyHasNoOpenQuestionsSignal { get; init; }

    /// <summary>
    /// True when the front-matter still says <c>intent_state: open</c> but the
    /// body explicitly records no current blockers / open questions. Callers
    /// should treat this as a non-blocking state for next-slice publish and
    /// surface a <c>stale-clarification-metadata</c> warning so the host can
    /// repair the file later.
    /// </summary>
    public bool StaleClarificationMetadata =>
        string.Equals(FrontMatterState, "open", StringComparison.OrdinalIgnoreCase)
        && !HasOpenBlocker
        && (BodyHasNoBlockerSignal || BodyHasNoOpenQuestionsSignal);
}
