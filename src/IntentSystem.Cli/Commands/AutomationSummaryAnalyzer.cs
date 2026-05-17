namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only analyzer for the <c>intent-cli automation summary</c> command (G186).
/// Optionally reads a parent <c>intents/&lt;domain&gt;/automation/bindings.md</c>
/// (under <see cref="CliContext.ResolveParentIntentRepoRootPath"/>, falling back
/// to <see cref="CliContext.RepoRoot"/> when no parent root is configured) and
/// emits the canonical label-driven automation contract. Never mutates queue
/// state, runs, GitHub, packet files, source files, or any other on-disk state.
/// </summary>
internal static class AutomationSummaryAnalyzer
{
    public static AutomationSummaryResult Analyze(CliContext context, string? domainOverride)
    {
        ArgumentNullException.ThrowIfNull(context);

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride;

        var warnings = new List<string>();
        var bindings = LoadBindings(context, domain, warnings);

        // G346: resolve effective base branch policy from host config; expose
        // as stable fields so AI threads can derive the expected PR base branch
        // without reading prompt memory or host `.intent-cli` directly.
        var effectivePolicy = context.Config.Project.BaseBranchPolicy;
        var configuredImplementationBaseBranch = context.Config.Project.ImplementationBaseBranch;
        // G362: prefer the explicitly configured ImplementationBaseBranch
        // (G350 field) over the policy-derived default so same-repo
        // topology with `main-ai` etc. surfaces the correct branch.
        var implementationBaseBranch = string.IsNullOrWhiteSpace(configuredImplementationBaseBranch)
            ? BaseBranchPolicyContract.ResolveExpectedBaseBranch(effectivePolicy)
            : configuredImplementationBaseBranch;

        // G362: surface metadata source/write branch fields when the
        // host is configured for same-repo topology. When neither
        // explicit branch is set, fall back to the legacy
        // MetadataBranch field (G350); both empty signals "no
        // same-repo gates in play" and the loop keeps its pre-G362
        // pull-first main behavior (G357).
        var sameRepoTopology = context.Config.Project.SameRepoTopology;
        var metadataSourceBranch = ResolveMetadataBranch(
            context.Config.Project.MetadataSourceBranch,
            context.Config.Project.MetadataBranch);
        var metadataWriteBranch = ResolveMetadataBranch(
            context.Config.Project.MetadataWriteBranch,
            context.Config.Project.MetadataBranch);

        return new AutomationSummaryResult
        {
            Domain = domain,
            Repo = bindings.Repo,
            SubmodulePath = bindings.SubmodulePath,
            QueueStatePath = bindings.QueueStatePath,
            RunsLogPath = bindings.RunsLogPath,
            PacketRoot = bindings.PacketRoot,
            ExecutionUnitRegex = bindings.ExecutionUnitRegex,
            EffectiveBaseBranchPolicy = effectivePolicy,
            ImplementationBaseBranch = implementationBaseBranch,
            SameRepoTopology = sameRepoTopology,
            MetadataSourceBranch = metadataSourceBranch,
            MetadataWriteBranch = metadataWriteBranch,
            IssueWorkflowLabels = AutomationSummaryConstants.IssueWorkflowLabels,
            PrWorkflowLabels = AutomationSummaryConstants.PrWorkflowLabels,
            HostLoopResponsibilities = AutomationSummaryConstants.HostLoopResponsibilities,
            HostPrTransitionCommands = AutomationSummaryConstants.HostPrTransitionCommands,
            AutomationCapabilitySchemaVersion = AutomationSummaryConstants.AutomationCapabilitySchemaVersion,
            AutomationCommandSurfaceVersion = AutomationSummaryConstants.AutomationCommandSurfaceVersion,
            AutomationCommandCapabilities = AutomationSummaryConstants.AutomationCommandCapabilities,
            ChildLoopResponsibilities = AutomationSummaryConstants.ChildLoopResponsibilities,
            PublishBoundaryGuidance = AutomationSummaryConstants.PublishBoundaryGuidance,
            WipCapGuidance = AutomationSummaryConstants.WipCapGuidance,
            Warnings = warnings
        };
    }

    /// <summary>
    /// G362: prefer the explicit per-role branch field
    /// (<see cref="ProjectConfig.MetadataSourceBranch"/> /
    /// <see cref="ProjectConfig.MetadataWriteBranch"/>) when set;
    /// otherwise fall back to the single-field
    /// <see cref="ProjectConfig.MetadataBranch"/> (G350). Returns
    /// empty string when neither is configured — callers MUST treat
    /// that as "no same-repo gate in play".
    /// </summary>
    private static string ResolveMetadataBranch(string roleSpecific, string legacy)
    {
        if (!string.IsNullOrWhiteSpace(roleSpecific))
        {
            return roleSpecific.Trim();
        }
        return string.IsNullOrWhiteSpace(legacy) ? string.Empty : legacy.Trim();
    }

    private static ParsedBindings LoadBindings(
        CliContext context,
        string domain,
        List<string> warnings)
    {
        var bindingsRelPath = ResolveBindingsRelativePath(domain);
        var bindingsAbsPath = ResolveBindingsAbsolutePath(context, domain);

        if (string.IsNullOrEmpty(bindingsAbsPath) || !File.Exists(bindingsAbsPath))
        {
            warnings.Add($"missing parent bindings file: {bindingsRelPath}");
            return ParsedBindings.Empty;
        }

        try
        {
            var content = File.ReadAllText(bindingsAbsPath);
            return ParseBindings(content, warnings);
        }
        catch (IOException exception)
        {
            warnings.Add($"could not read parent bindings file: {exception.Message}");
            return ParsedBindings.Empty;
        }
        catch (UnauthorizedAccessException exception)
        {
            warnings.Add($"could not read parent bindings file: {exception.Message}");
            return ParsedBindings.Empty;
        }
    }

    private static string ResolveBindingsRelativePath(string domain)
    {
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain;
        return Path.Combine("intents", safeDomain, "automation", "bindings.md");
    }

    private static string? ResolveBindingsAbsolutePath(CliContext context, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var parentRoot = context.ResolveParentIntentRepoRootPath();
        var baseRoot = string.IsNullOrWhiteSpace(parentRoot)
            ? context.RepoRoot
            : parentRoot;

        if (string.IsNullOrWhiteSpace(baseRoot))
        {
            return null;
        }

        return Path.Combine(baseRoot, "intents", domain, "automation", "bindings.md");
    }

    /// <summary>
    /// Minimal deterministic parser for the small subset of bindings.md fields
    /// we surface. Tolerates both YAML frontmatter (delimited by <c>---</c>) and
    /// inline <c>key: value</c> lines. Recognizes both "logical" key names from
    /// the issue spec (<c>repo</c>, <c>submodule_path</c>, <c>queue_state_path</c>,
    /// <c>runs_log_path</c>, <c>packet_root</c>) and the legacy aliases observed
    /// in existing bindings files (<c>child_repo</c>, <c>child_submodule_path</c>,
    /// <c>queue_file</c>, <c>runs_file</c>, <c>packet_dir</c>). Never throws on
    /// malformed input — surfaces a warning instead.
    /// </summary>
    private static ParsedBindings ParseBindings(string content, List<string> warnings)
    {
        var bindings = new ParsedBindings();

        try
        {
            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
            var lines = normalized.Split('\n');

            // Track whether we're inside the YAML frontmatter region. We still
            // accept inline key:value lines outside frontmatter for tolerance.
            var sawFrontmatterOpen = false;
            var insideFrontmatter = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (line.Length == 0)
                {
                    continue;
                }

                if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
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

                // Skip indented (nested) lines; we only consume top-level keys.
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    continue;
                }

                // Skip markdown headings, list markers, comments.
                if (line.StartsWith('#') || line.StartsWith("- ", StringComparison.Ordinal))
                {
                    continue;
                }

                var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
                if (colonIndex <= 0)
                {
                    continue;
                }

                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();

                // Strip surrounding quotes if present.
                if (value.Length >= 2
                    && ((value[0] == '\'' && value[^1] == '\'')
                        || (value[0] == '"' && value[^1] == '"')))
                {
                    value = value[1..^1];
                }

                if (value.Length == 0)
                {
                    continue;
                }

                AssignField(bindings, key, value);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or FormatException)
        {
            warnings.Add($"could not parse bindings file: {exception.Message}");
        }

        return bindings;
    }

    private static void AssignField(ParsedBindings bindings, string key, string value)
    {
        switch (key)
        {
            case "repo":
            case "child_repo":
                bindings.Repo ??= value;
                break;
            case "submodule_path":
            case "child_submodule_path":
                bindings.SubmodulePath ??= value;
                break;
            case "queue_state_path":
            case "queue_file":
                bindings.QueueStatePath ??= value;
                break;
            case "runs_log_path":
            case "runs_file":
                bindings.RunsLogPath ??= value;
                break;
            case "packet_root":
            case "packet_dir":
                bindings.PacketRoot ??= value;
                break;
            case "execution_unit_regex":
                bindings.ExecutionUnitRegex ??= value;
                break;
        }
    }

    private sealed class ParsedBindings
    {
        public static ParsedBindings Empty { get; } = new ParsedBindings();

        public string? Repo { get; set; }
        public string? SubmodulePath { get; set; }
        public string? QueueStatePath { get; set; }
        public string? RunsLogPath { get; set; }
        public string? PacketRoot { get; set; }
        public string? ExecutionUnitRegex { get; set; }
    }
}
