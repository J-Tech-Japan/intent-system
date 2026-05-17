using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G361: pure analyzer that classifies a prepared packet directory under
/// <c>.intent-cli/issues/&lt;execution-unit&gt;/</c> as either a safe
/// commit-ready durable-state candidate or a structurally unsafe one.
///
/// A packet directory is <c>commit-ready</c> when ALL of the following
/// hold:
/// <list type="bullet">
/// <item>The four canonical files exist: <c>packet.yaml</c>,
///   <c>implementation.md</c>, <c>review-context.md</c>,
///   <c>github-body.md</c>.</item>
/// <item><c>packet.yaml</c> parses as a YAML scalar map.</item>
/// <item>When the host supplies a domain binding
///   <see cref="PreparedPacketCommitReadyInput.ExecutionUnitRegex"/>,
///   the directory-derived execution-unit MUST match it (rejects
///   cross-domain packets the host loop must not commit on behalf of
///   the active domain).</item>
/// <item>When the host supplies
///   <see cref="PreparedPacketCommitReadyInput.RequestedTargetRepo"/>,
///   the packet's declared <c>target_repo</c>
///   (under either <c>implementation_issue.target_repo</c> or a
///   top-level <c>target_repo</c>) MUST match it (rejects packets that
///   would publish into a different child repo).</item>
/// <item><c>github-body.md</c> carries the required standalone issue
///   sections (mirrors
///   <see cref="MetadataValidateConstants.RequiredGithubBodySections"/>)
///   so that a downstream <c>issue publish-flow</c> can publish without
///   operator hand-editing.</item>
/// </list>
///
/// Anything else is <c>unsafe-prepared-packet</c> with a structured
/// reason: <c>missing-canonical-file</c>, <c>packet-yaml-unparseable</c>,
/// <c>wrong-domain</c>, <c>wrong-target-repo</c>, or
/// <c>github-body-missing-section</c>.
///
/// Pure data in / pure data out: callers (the durable-state preflight
/// command's probe in particular) read the working-tree files, resolve
/// the active domain binding regex, and feed the result here. Analyzer
/// never touches disk or git.
/// </summary>
internal static class PreparedPacketCommitReadyAnalyzer
{
    public const string ClassificationCommitReady = "prepared-packet-commit-ready";
    public const string ClassificationUnsafe = "unsafe-prepared-packet";

    public const string ReasonMissingCanonicalFile = "missing-canonical-file";
    public const string ReasonPacketYamlUnparseable = "packet-yaml-unparseable";
    public const string ReasonWrongDomain = "wrong-domain";
    public const string ReasonWrongTargetRepo = "wrong-target-repo";
    public const string ReasonGithubBodyMissingSection = "github-body-missing-section";
    public const string ReasonMalformedExecutionUnit = "malformed-execution-unit";

    /// <summary>
    /// PR #824 review repair #2: the host requested a domain binding
    /// (<see cref="PreparedPacketCommitReadyInput.RequireDomainBinding"/>
    /// was true) but the bindings.md file or its
    /// <c>execution_unit_regex</c> field is missing. Refusing the
    /// packet here is the only way to guarantee a packet is never
    /// auto-committed without verifying the active domain boundary.
    /// </summary>
    public const string ReasonMissingDomainBindingRegex = "missing-domain-binding-regex";

    /// <summary>
    /// PR #824 review repair #2: bindings.md declared an
    /// <c>execution_unit_regex</c> but it does not compile (operator
    /// typo, missing escape, etc.). Refusing the packet here is the
    /// only way to guarantee a malformed bindings file can never
    /// silently bypass the cross-domain check.
    /// </summary>
    public const string ReasonInvalidDomainBindingRegex = "invalid-domain-binding-regex";

    /// <summary>
    /// PR #824 review repair #7: the caller did not supply a
    /// requested target repo (`--target-repo` flag), so the
    /// prepared-packet lane cannot validate that the packet's
    /// declared <c>target_repo</c> matches the intended publication
    /// target. The lane MUST refuse rather than silently accept the
    /// packet as commit-ready — without that validation a packet
    /// aimed at a different child repo could still be auto-committed.
    /// </summary>
    public const string ReasonMissingTargetRepo = "missing-target-repo";

    public const string FileNamePacketYaml = "packet.yaml";
    public const string FileNameImplementationMarkdown = "implementation.md";
    public const string FileNameReviewContextMarkdown = "review-context.md";
    public const string FileNameGithubBodyMarkdown = "github-body.md";

    public static readonly IReadOnlyList<string> CanonicalFileNames = new[]
    {
        FileNamePacketYaml,
        FileNameImplementationMarkdown,
        FileNameReviewContextMarkdown,
        FileNameGithubBodyMarkdown,
    };

    /// <summary>
    /// PR #824 review repair #5: canonical github-body section
    /// headings the analyzer requires for commit-ready
    /// classification. The match is EXACT (case-insensitive after
    /// trimming trailing punctuation), so non-standalone variants
    /// (`## My Goal`, `## Goal - notes`) are rejected. The list
    /// mirrors the IntentNextSliceCommand contract sections and the
    /// canonical github-body shape produced by the packet draft
    /// surface — including the full `Target Repo / Path / Part`
    /// heading rather than the looser MetadataValidate substring
    /// `Target Repo`.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredGithubBodySections = new[]
    {
        "Goal",
        "Why This Slice Exists Now",
        "Current Observed State",
        "Accepted Baseline You May Assume",
        "Target Repo / Path / Part",
        "In Scope",
        "Out Of Scope",
        "Acceptance Criteria",
        "Verification",
        "Related Links",
    };

    public static PreparedPacketCommitReadyResult Analyze(PreparedPacketCommitReadyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ExecutionUnit);

        var packetDirectory = $".intent-cli/issues/{input.ExecutionUnit}/";

        // 1. Missing canonical files -------------------------------------
        var missing = new List<string>();
        if (input.PacketYaml is null)
        {
            missing.Add(packetDirectory + FileNamePacketYaml);
        }
        if (input.ImplementationMarkdown is null)
        {
            missing.Add(packetDirectory + FileNameImplementationMarkdown);
        }
        if (input.ReviewContextMarkdown is null)
        {
            missing.Add(packetDirectory + FileNameReviewContextMarkdown);
        }
        if (input.GithubBodyMarkdown is null)
        {
            missing.Add(packetDirectory + FileNameGithubBodyMarkdown);
        }
        if (missing.Count > 0)
        {
            return new PreparedPacketCommitReadyResult
            {
                Classification = ClassificationUnsafe,
                Reason = ReasonMissingCanonicalFile,
                ExecutionUnit = input.ExecutionUnit,
                PacketDirectory = packetDirectory,
                MissingFiles = missing,
                Summary = $"prepared packet `{packetDirectory}` is missing canonical file(s): "
                    + string.Join(", ", missing)
                    + ". A complete packet must carry packet.yaml, implementation.md, review-context.md, and github-body.md before host-loop auto-commit.",
            };
        }

        // 2. Domain binding regex check ---------------------------------
        // PR #824 review repair #2: when the host requests a domain
        // binding (`RequireDomainBinding == true`), missing or
        // malformed `execution_unit_regex` is an UNSAFE STOP — accepting
        // a packet without verifying the domain boundary defeats the
        // purpose of the lane. Only when the host explicitly opted out
        // of cross-domain scoping (RequireDomainBinding == false AND no
        // regex supplied) is the check skipped.
        if (input.RequireDomainBinding && string.IsNullOrWhiteSpace(input.ExecutionUnitRegex))
        {
            return new PreparedPacketCommitReadyResult
            {
                Classification = ClassificationUnsafe,
                Reason = ReasonMissingDomainBindingRegex,
                ExecutionUnit = input.ExecutionUnit,
                PacketDirectory = packetDirectory,
                Summary = $"prepared packet `{packetDirectory}` cannot be classified commit-ready: "
                    + "the active domain has no `execution_unit_regex` in its bindings.md "
                    + "(either the bindings file is missing or the field is absent). Host-loop "
                    + "auto-commit refuses to accept a prepared packet without verifying the "
                    + "active domain boundary.",
            };
        }

        if (!string.IsNullOrWhiteSpace(input.ExecutionUnitRegex))
        {
            Regex regex;
            try
            {
                regex = new Regex(input.ExecutionUnitRegex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException exception)
            {
                if (input.RequireDomainBinding)
                {
                    // PR #824 review repair #2: fail closed on an
                    // unparseable binding regex when the host requested
                    // the binding. A malformed bindings.md must never
                    // silently bypass the cross-domain check.
                    return new PreparedPacketCommitReadyResult
                    {
                        Classification = ClassificationUnsafe,
                        Reason = ReasonInvalidDomainBindingRegex,
                        ExecutionUnit = input.ExecutionUnit,
                        PacketDirectory = packetDirectory,
                        DomainRegex = input.ExecutionUnitRegex,
                        Summary = $"prepared packet `{packetDirectory}` cannot be classified commit-ready: "
                            + $"the active domain's `execution_unit_regex` `{input.ExecutionUnitRegex}` does not "
                            + $"compile ({exception.Message}). Fix the bindings.md pattern before "
                            + "the host loop can verify the domain boundary.",
                    };
                }
                // Legacy fail-open path retained ONLY when the host did
                // not require a binding (no --domain configured).
                regex = null!;
            }

            if (regex is not null && !regex.IsMatch(input.ExecutionUnit))
            {
                return new PreparedPacketCommitReadyResult
                {
                    Classification = ClassificationUnsafe,
                    Reason = ReasonWrongDomain,
                    ExecutionUnit = input.ExecutionUnit,
                    PacketDirectory = packetDirectory,
                    DomainRegex = input.ExecutionUnitRegex,
                    Summary = $"prepared packet `{packetDirectory}` declares execution-unit `{input.ExecutionUnit}` "
                        + $"which does not match the active domain binding regex `{input.ExecutionUnitRegex}`; "
                        + "host-loop auto-commit must not publish cross-domain packet directories.",
                };
            }
        }

        // 3. packet.yaml parses -----------------------------------------
        IReadOnlyDictionary<string, string> packetFields;
        try
        {
            packetFields = PreparedPacketYamlScalarParser.Parse(input.PacketYaml!);
        }
        catch (FormatException exception)
        {
            return new PreparedPacketCommitReadyResult
            {
                Classification = ClassificationUnsafe,
                Reason = ReasonPacketYamlUnparseable,
                ExecutionUnit = input.ExecutionUnit,
                PacketDirectory = packetDirectory,
                Summary = $"prepared packet `{packetDirectory}` packet.yaml does not parse "
                    + $"as a scalar YAML map: {exception.Message}. Operator review required before auto-commit.",
            };
        }

        // 4. Target repo check ------------------------------------------
        // PR #824 review repair #7: the prepared-packet lane MUST NOT
        // classify a packet as commit-ready without verifying its
        // declared `target_repo` matches the host's intended
        // publication target. The caller is required to supply
        // `RequestedTargetRepo`; refuse otherwise so an invocation
        // that reaches this lane without `--target-repo` cannot
        // bypass the child-repo binding check.
        if (string.IsNullOrWhiteSpace(input.RequestedTargetRepo))
        {
            return new PreparedPacketCommitReadyResult
            {
                Classification = ClassificationUnsafe,
                Reason = ReasonMissingTargetRepo,
                ExecutionUnit = input.ExecutionUnit,
                PacketDirectory = packetDirectory,
                Summary = $"prepared packet `{packetDirectory}` cannot be classified commit-ready: "
                    + "no `--target-repo` was supplied, so the analyzer cannot verify the packet's "
                    + "declared `target_repo` matches the intended child repo. Re-run "
                    + "`automation durable-state-preflight --target-repo <owner/repo>` "
                    + "(and `--domain <name>` for cross-domain scoping).",
            };
        }

        if (!string.IsNullOrWhiteSpace(input.RequestedTargetRepo))
        {
            var declaredRepo = LookupScalar(
                packetFields,
                "implementation_issue.target_repo",
                "target_repo",
                "implementation_issue_packet.target_repo");
            if (string.IsNullOrWhiteSpace(declaredRepo))
            {
                return new PreparedPacketCommitReadyResult
                {
                    Classification = ClassificationUnsafe,
                    Reason = ReasonWrongTargetRepo,
                    ExecutionUnit = input.ExecutionUnit,
                    PacketDirectory = packetDirectory,
                    RequestedTargetRepo = input.RequestedTargetRepo,
                    DeclaredTargetRepo = null,
                    Summary = $"prepared packet `{packetDirectory}` does not declare a target_repo "
                        + $"while the host loop expects target_repo=`{input.RequestedTargetRepo}`; host-loop "
                        + "auto-commit refuses ambiguous publication target.",
                };
            }
            if (!string.Equals(declaredRepo, input.RequestedTargetRepo, StringComparison.Ordinal))
            {
                return new PreparedPacketCommitReadyResult
                {
                    Classification = ClassificationUnsafe,
                    Reason = ReasonWrongTargetRepo,
                    ExecutionUnit = input.ExecutionUnit,
                    PacketDirectory = packetDirectory,
                    RequestedTargetRepo = input.RequestedTargetRepo,
                    DeclaredTargetRepo = declaredRepo,
                    Summary = $"prepared packet `{packetDirectory}` declares target_repo=`{declaredRepo}` "
                        + $"but host loop expects `{input.RequestedTargetRepo}`; host-loop auto-commit refuses "
                        + "to publish a packet aimed at a different child repo.",
                };
            }
        }

        // 5. github-body.md required sections ---------------------------
        // PR #824 review repair #5: use the analyzer's own
        // RequiredGithubBodySections (with the full canonical heading
        // names) plus the strict exact-match HasMatchingHeading helper
        // so non-standalone headings like `## My Goal` are rejected.
        var headings = ExtractHeadings(input.GithubBodyMarkdown!);
        foreach (var section in RequiredGithubBodySections)
        {
            if (!HasMatchingHeading(headings, section))
            {
                return new PreparedPacketCommitReadyResult
                {
                    Classification = ClassificationUnsafe,
                    Reason = ReasonGithubBodyMissingSection,
                    ExecutionUnit = input.ExecutionUnit,
                    PacketDirectory = packetDirectory,
                    MissingGithubBodySection = section,
                    Summary = $"prepared packet `{packetDirectory}` github-body.md is missing required "
                        + $"section `{section}`; host-loop auto-commit refuses an incomplete child issue contract.",
                };
            }
        }

        // 6. Commit-ready -----------------------------------------------
        return new PreparedPacketCommitReadyResult
        {
            Classification = ClassificationCommitReady,
            ExecutionUnit = input.ExecutionUnit,
            PacketDirectory = packetDirectory,
            VerifiedFiles = CanonicalFileNames.Select(name => packetDirectory + name).ToArray(),
            RequestedTargetRepo = input.RequestedTargetRepo,
            DeclaredTargetRepo = LookupScalar(
                packetFields,
                "implementation_issue.target_repo",
                "target_repo",
                "implementation_issue_packet.target_repo"),
            DomainRegex = input.ExecutionUnitRegex,
            Summary = $"prepared packet `{packetDirectory}` has the four canonical files, packet.yaml parses, "
                + "github-body.md carries the required standalone sections, and the domain/target-repo bindings match; "
                + "safe to commit before downstream issue publish (G361).",
        };
    }

    private static string? LookupScalar(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ExtractHeadings(string markdown)
    {
        var headings = new List<string>();
        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '#')
            {
                continue;
            }
            var depth = 0;
            while (depth < trimmed.Length && depth < 6 && trimmed[depth] == '#')
            {
                depth++;
            }
            if (depth == 0 || depth >= trimmed.Length)
            {
                continue;
            }
            if (trimmed[depth] != ' ' && trimmed[depth] != '\t')
            {
                continue;
            }
            headings.Add(trimmed[(depth + 1)..].Trim());
        }
        return headings;
    }

    /// <summary>
    /// PR #824 review repair #5: match required sections by EXACT
    /// heading text (case-insensitive) instead of substring contains.
    /// The substring match accepted non-standalone headings like
    /// `## My Goal` or `## Goal - notes` and would have let a packet
    /// pass without the required `## Goal` section. Trailing
    /// punctuation that the markdown renderer ignores
    /// (colon / period / trailing whitespace) is tolerated so that
    /// `## Goal:` still satisfies `Goal`, but partial-word matches
    /// like `My Goal` are rejected.
    /// </summary>
    private static bool HasMatchingHeading(IReadOnlyList<string> headings, string section)
    {
        foreach (var heading in headings)
        {
            var normalized = heading.TrimEnd().TrimEnd(':', '.', '!', '?').TrimEnd();
            if (string.Equals(normalized, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// G361: structured input for
/// <see cref="PreparedPacketCommitReadyAnalyzer.Analyze"/>.
/// </summary>
internal sealed record PreparedPacketCommitReadyInput
{
    /// <summary>
    /// Directory-derived execution unit (e.g. <c>Z4R-G3</c>). REQUIRED.
    /// Caller reads it off the <c>.intent-cli/issues/&lt;unit&gt;/</c>
    /// directory name, never from the packet body.
    /// </summary>
    public required string ExecutionUnit { get; init; }

    /// <summary>Content of <c>packet.yaml</c>, or null when missing.</summary>
    public string? PacketYaml { get; init; }

    /// <summary>Content of <c>implementation.md</c>, or null when missing.</summary>
    public string? ImplementationMarkdown { get; init; }

    /// <summary>Content of <c>review-context.md</c>, or null when missing.</summary>
    public string? ReviewContextMarkdown { get; init; }

    /// <summary>Content of <c>github-body.md</c>, or null when missing.</summary>
    public string? GithubBodyMarkdown { get; init; }

    /// <summary>
    /// Active domain binding <c>execution_unit_regex</c> (from
    /// <c>intents/&lt;domain&gt;/automation/bindings.md</c>). Null when
    /// the host has not configured one; the analyzer skips the
    /// cross-domain check in that case.
    /// </summary>
    public string? ExecutionUnitRegex { get; init; }

    /// <summary>
    /// Target repo the host loop intends to publish into (e.g.
    /// <c>J-Tech-Creations/Zero4Racer</c>). Null when not specified;
    /// the analyzer skips the target-repo check in that case.
    /// </summary>
    public string? RequestedTargetRepo { get; init; }

    /// <summary>
    /// PR #824 review repair #2: when true, the host explicitly
    /// requires a domain binding (`--domain` was supplied to the
    /// preflight). Missing or malformed
    /// <see cref="ExecutionUnitRegex"/> becomes an unsafe stop in
    /// that case (<see cref="PreparedPacketCommitReadyAnalyzer.ReasonMissingDomainBindingRegex"/>
    /// / <see cref="PreparedPacketCommitReadyAnalyzer.ReasonInvalidDomainBindingRegex"/>)
    /// — accepting a packet without verifying the active domain
    /// boundary defeats the purpose of the lane. When false (no
    /// domain configured), the legacy fail-open behavior is retained
    /// for backward compatibility with hosts that have not opted into
    /// cross-domain scoping.
    /// </summary>
    public bool RequireDomainBinding { get; init; }
}

/// <summary>
/// G361: result emitted by
/// <see cref="PreparedPacketCommitReadyAnalyzer.Analyze"/>. The terminal
/// <see cref="Classification"/> is either
/// <see cref="PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady"/>
/// or
/// <see cref="PreparedPacketCommitReadyAnalyzer.ClassificationUnsafe"/>.
/// </summary>
internal sealed record PreparedPacketCommitReadyResult
{
    public required string Classification { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string PacketDirectory { get; init; }
    public required string Summary { get; init; }

    /// <summary>Structured unsafe reason; null when <see cref="Classification"/> is commit-ready.</summary>
    public string? Reason { get; init; }

    public IReadOnlyList<string>? MissingFiles { get; init; }
    public string? MissingGithubBodySection { get; init; }
    public string? DomainRegex { get; init; }
    public string? RequestedTargetRepo { get; init; }
    public string? DeclaredTargetRepo { get; init; }

    /// <summary>Set on commit-ready: full relative paths the host loop should stage.</summary>
    public IReadOnlyList<string>? VerifiedFiles { get; init; }
}

/// <summary>
/// G361 helper: minimal indentation-tracked YAML scalar parser
/// extracted from <see cref="MetadataValidateAnalyzer"/> so the
/// prepared-packet analyzer can read the small set of fields it needs
/// (<c>target_repo</c> / <c>implementation_issue.target_repo</c>)
/// without taking a dependency on the much larger validate analyzer.
/// </summary>
internal static class PreparedPacketYamlScalarParser
{
    private static readonly System.Text.RegularExpressions.Regex YamlScalarKeyRegex = new(
        // Mirrors MetadataValidateAnalyzer.YamlScalarKeyRegex (single source
        // of truth would be ideal, but the validator keeps it private — we
        // intentionally duplicate the pattern with the same correctness
        // notes around using [ \t]* instead of \s* before the value
        // capture so a value-less line doesn't slurp the next line).
        @"^(?<indent>[ \t]*)(?<key>[A-Za-z_][A-Za-z0-9_\-]*)[ \t]*:[ \t]*(?<value>.*)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>
    /// Parse a packet.yaml scalar map. PR #824 review repair #2:
    /// validates each non-blank, non-comment line for basic YAML
    /// syntax conformance and throws <see cref="FormatException"/>
    /// when a malformed line is encountered. This makes
    /// <see cref="PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable"/>
    /// reachable for files that the legacy line-scanner would have
    /// silently ignored.
    ///
    /// Rejected as malformed (fail-closed):
    /// <list type="bullet">
    /// <item>Tab characters anywhere in the leading indentation
    ///   (YAML 1.2 §6.1 forbids tab indentation).</item>
    /// <item>Lines that have neither a colon nor are a comment / list
    ///   marker / frontmatter delimiter (`key value` with no colon).</item>
    /// <item>A line that starts with a colon (missing key).</item>
    /// <item>A value containing an unbalanced single or double quote
    ///   (operator typo that breaks scalar parsing).</item>
    /// </list>
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var pathStack = new List<(int Indent, string Key)>();
        var normalized = yaml.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var insideFrontmatter = false;
        var sawFrontmatterOpen = false;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            var lineNumber = lineIndex + 1;

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            // Tab in leading whitespace is a YAML syntax error (1.2 §6.1).
            for (var charIndex = 0; charIndex < rawLine.Length; charIndex++)
            {
                var ch = rawLine[charIndex];
                if (ch == ' ')
                {
                    continue;
                }
                if (ch == '\t')
                {
                    throw new FormatException(
                        $"line {lineNumber}: tab character in indentation is invalid YAML (use spaces).");
                }
                break;
            }

            var trimmedStart = rawLine.TrimStart();

            // Comments are allowed and ignored.
            if (trimmedStart.StartsWith('#'))
            {
                continue;
            }

            // List markers and document delimiters are ignored (frontmatter).
            if (string.Equals(trimmedStart.TrimEnd(), "---", StringComparison.Ordinal))
            {
                if (!sawFrontmatterOpen)
                {
                    sawFrontmatterOpen = true;
                    insideFrontmatter = true;
                }
                else if (insideFrontmatter)
                {
                    insideFrontmatter = false;
                }
                continue;
            }
            if (trimmedStart.StartsWith("- ", StringComparison.Ordinal)
                || string.Equals(trimmedStart.TrimEnd(), "-", StringComparison.Ordinal))
            {
                continue;
            }

            var match = YamlScalarKeyRegex.Match(rawLine);
            if (!match.Success)
            {
                throw new FormatException(
                    $"line {lineNumber}: malformed YAML (expected `key: value` or comment / list marker / frontmatter delimiter): `{rawLine.Trim()}`.");
            }

            var indent = match.Groups["indent"].Value.Length;
            var key = match.Groups["key"].Value;
            var rawValue = match.Groups["value"].Value;
            var hadInlineValue = rawValue.Length > 0 && !rawValue.StartsWith('#');
            var value = rawValue;
            var hashIndex = value.IndexOf(" #", StringComparison.Ordinal);
            if (hashIndex >= 0)
            {
                value = value[..hashIndex];
            }
            value = value.Trim();

            // Fail closed on unbalanced quotes in the value scalar.
            if (HasUnbalancedQuote(value, '\''))
            {
                throw new FormatException(
                    $"line {lineNumber}: unbalanced single quote in value for `{key}`: `{rawValue.Trim()}`.");
            }
            if (HasUnbalancedQuote(value, '"'))
            {
                throw new FormatException(
                    $"line {lineNumber}: unbalanced double quote in value for `{key}`: `{rawValue.Trim()}`.");
            }

            if (value.Length >= 2
                && (value[0] == '"' && value[^1] == '"'
                    || value[0] == '\'' && value[^1] == '\''))
            {
                value = value.Substring(1, value.Length - 2);
            }

            while (pathStack.Count > 0 && pathStack[^1].Indent >= indent)
            {
                pathStack.RemoveAt(pathStack.Count - 1);
            }

            var dottedPath = pathStack.Count == 0
                ? key
                : string.Join(".", pathStack.Select(e => e.Key)) + "." + key;

            if (hadInlineValue && !string.IsNullOrEmpty(value))
            {
                fields[dottedPath] = value;
                if (!fields.ContainsKey(key))
                {
                    fields[key] = value;
                }
            }
            else
            {
                pathStack.Add((indent, key));
            }
        }

        return fields;
    }

    private static bool HasUnbalancedQuote(string value, char quote)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        var count = 0;
        foreach (var ch in value)
        {
            if (ch == quote)
            {
                count++;
            }
        }
        return (count % 2) != 0;
    }
}
