using IntentSystem.Cli.Models;
using IntentSystem.Cli.Commands;
using Tomlyn;
using Tomlyn.Model;

namespace IntentSystem.Cli.Infrastructure;

internal static class CliConfigLoader
{
    public static CliConfig Load(string toml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toml);

        var model = TomlSerializer.Deserialize<TomlTable>(toml);
        if (model is not TomlTable rootTable)
        {
            throw new InvalidOperationException("CLI config payload did not deserialize to a TOML table.");
        }

        if (TryCreateFromRootKeys(rootTable, out var rootConfig))
        {
            return rootConfig;
        }

        if (TryCreateFromProjectSection(rootTable, out var projectConfig))
        {
            return projectConfig;
        }

        throw new InvalidOperationException(
            $"CLI config must contain root keys '{CliRuntimeContracts.DefaultDomainKey}', " +
            $"and '{CliRuntimeContracts.ArtifactRootKey}'.");
    }

    public static CliConfig LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Load(File.ReadAllText(filePath));
    }

    private static bool TryCreateFromRootKeys(TomlTable rootTable, out CliConfig config)
    {
        config = default!;

        if (!TryGetRequiredString(rootTable, CliRuntimeContracts.DefaultDomainKey, out var domain)
            || !TryGetRequiredString(rootTable, CliRuntimeContracts.ArtifactRootKey, out var artifactRoot))
        {
            return false;
        }

        RejectObsoleteWorkflowEngineKey(rootTable, "CLI config root");

        var worktreeRoot = TryGetOptionalString(rootTable, CliRuntimeContracts.WorktreeRootKey)
            ?? CliRuntimeContracts.DefaultWorktreeRoot;
        var workRepoPath = TryGetOptionalString(rootTable, CliRuntimeContracts.WorkRepoPathKey)
            ?? string.Empty;
        var parentIntentRepoRoot = TryGetOptionalString(rootTable, CliRuntimeContracts.ParentIntentRepoRootKey)
            ?? string.Empty;
        var baseBranchPolicy = ReadBaseBranchPolicy(rootTable, "CLI config root");
        var branchLanes = ReadBranchLanes(rootTable, "CLI config root", domain);
        var stableBranch = TryGetOptionalString(rootTable, CliRuntimeContracts.StableBranchKey)
            ?? string.Empty;
        var implementationBaseBranch = TryGetOptionalString(rootTable, CliRuntimeContracts.ImplementationBaseBranchKey)
            ?? string.Empty;
        var metadataBranch = TryGetOptionalString(rootTable, CliRuntimeContracts.MetadataBranchKey)
            ?? string.Empty;
        // G362: same-repo topology fields.
        var metadataSourceBranch = TryGetOptionalString(rootTable, CliRuntimeContracts.MetadataSourceBranchKey)
            ?? string.Empty;
        var metadataWriteBranch = TryGetOptionalString(rootTable, CliRuntimeContracts.MetadataWriteBranchKey)
            ?? string.Empty;
        var sameRepoTopology = TryGetOptionalBool(rootTable, CliRuntimeContracts.SameRepoTopologyKey) ?? false;
        var roles = ReadRoles(rootTable);
        var supervision = ReadSupervision(rootTable);
        var run = ReadRun(rootTable);
        var directRun = ReadDirectRun(rootTable);

        config = CreateConfig(
            domain,
            artifactRoot,
            worktreeRoot,
            workRepoPath,
            parentIntentRepoRoot,
            baseBranchPolicy,
            branchLanes,
            stableBranch,
            implementationBaseBranch,
            metadataBranch,
            metadataSourceBranch,
            metadataWriteBranch,
            sameRepoTopology,
            roles,
            supervision,
            run,
            directRun);
        return true;
    }

    private static bool TryCreateFromProjectSection(TomlTable rootTable, out CliConfig config)
    {
        config = default!;

        if (!rootTable.TryGetValue(CliRuntimeContracts.ProjectSectionName, out var projectSection)
            || projectSection is not TomlTable projectTable)
        {
            return false;
        }

        if (!TryGetRequiredString(projectTable, CliRuntimeContracts.DomainKey, out var domain)
            || !TryGetRequiredString(projectTable, CliRuntimeContracts.ArtifactRootKey, out var artifactRoot))
        {
            throw new InvalidOperationException(
                $"CLI config [project] section must contain '{CliRuntimeContracts.DomainKey}', " +
                $"and '{CliRuntimeContracts.ArtifactRootKey}'.");
        }

        RejectObsoleteWorkflowEngineKey(projectTable, "CLI config [project] section");

        var worktreeRoot = TryGetOptionalString(projectTable, CliRuntimeContracts.WorktreeRootKey)
            ?? CliRuntimeContracts.DefaultWorktreeRoot;
        var workRepoPath = TryGetOptionalString(projectTable, CliRuntimeContracts.WorkRepoPathKey)
            ?? string.Empty;
        var parentIntentRepoRoot = TryGetOptionalString(projectTable, CliRuntimeContracts.ParentIntentRepoRootKey)
            ?? string.Empty;
        var baseBranchPolicy = ReadBaseBranchPolicy(projectTable, "CLI config [project] section");
        var branchLanes = ReadBranchLanes(projectTable, "CLI config [project] section", domain);
        var stableBranch = TryGetOptionalString(projectTable, CliRuntimeContracts.StableBranchKey)
            ?? string.Empty;
        var implementationBaseBranch = TryGetOptionalString(projectTable, CliRuntimeContracts.ImplementationBaseBranchKey)
            ?? string.Empty;
        var metadataBranch = TryGetOptionalString(projectTable, CliRuntimeContracts.MetadataBranchKey)
            ?? string.Empty;
        // G362: same-repo topology fields, read from [project] section.
        var metadataSourceBranch = TryGetOptionalString(projectTable, CliRuntimeContracts.MetadataSourceBranchKey)
            ?? string.Empty;
        var metadataWriteBranch = TryGetOptionalString(projectTable, CliRuntimeContracts.MetadataWriteBranchKey)
            ?? string.Empty;
        var sameRepoTopology = TryGetOptionalBool(projectTable, CliRuntimeContracts.SameRepoTopologyKey) ?? false;
        var roles = ReadRoles(rootTable);
        var supervision = ReadSupervision(rootTable);
        var run = ReadRun(rootTable);
        var directRun = ReadDirectRun(rootTable);

        config = CreateConfig(
            domain,
            artifactRoot,
            worktreeRoot,
            workRepoPath,
            parentIntentRepoRoot,
            baseBranchPolicy,
            branchLanes,
            stableBranch,
            implementationBaseBranch,
            metadataBranch,
            metadataSourceBranch,
            metadataWriteBranch,
            sameRepoTopology,
            roles,
            supervision,
            run,
            directRun);
        return true;
    }

    private static CliConfig CreateConfig(
        string domain,
        string artifactRoot,
        string worktreeRoot,
        string workRepoPath,
        string parentIntentRepoRoot,
        string baseBranchPolicy,
        IReadOnlyDictionary<string, BranchLaneRegistry> branchLanes,
        string stableBranch,
        string implementationBaseBranch,
        string metadataBranch,
        string metadataSourceBranch,
        string metadataWriteBranch,
        bool sameRepoTopology,
        RoleMappings roles,
        SupervisionConfig supervision,
        RunConfig run,
        DirectRunConfig directRun)
    {
        return new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = domain,
                ArtifactRoot = artifactRoot,
                WorktreeRoot = worktreeRoot,
                WorkRepoPath = workRepoPath,
                ParentIntentRepoRoot = parentIntentRepoRoot,
                BaseBranchPolicy = baseBranchPolicy,
                BranchLanes = branchLanes,
                StableBranch = stableBranch,
                ImplementationBaseBranch = implementationBaseBranch,
                MetadataBranch = metadataBranch,
                MetadataSourceBranch = metadataSourceBranch,
                MetadataWriteBranch = metadataWriteBranch,
                SameRepoTopology = sameRepoTopology
            },
            Roles = roles,
            Supervision = supervision,
            Run = run,
            DirectRun = directRun
        };
    }

    private static string ReadBaseBranchPolicy(TomlTable table, string contractName)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        var value = TryGetOptionalString(table, CliRuntimeContracts.BaseBranchPolicyKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return CliRuntimeContracts.DefaultBaseBranchPolicy;
        }

        if (!string.Equals(value, CliRuntimeContracts.DirectMainBaseBranchPolicy, StringComparison.Ordinal)
            && !string.Equals(value, CliRuntimeContracts.MainAiBaseBranchPolicy, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{contractName} '{CliRuntimeContracts.BaseBranchPolicyKey}' must be '{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}' (got '{value}').");
        }

        return value;
    }

    /// <summary>
    /// G668: reads the preview named-lane registries. The canonical form is:
    ///
    /// <code>
    /// [project.branch_lanes.intent-cli]
    /// default_lane = "continuous"
    /// definition_revision = "r1"
    ///
    /// [project.branch_lanes.intent-cli.continuous]
    /// start_branch = "develop"
    /// pr_base_branch = "develop"
    /// landing_mode = "direct"
    /// </code>
    ///
    /// Root-level <c>[branch_lanes.&lt;domain&gt;]</c> is accepted for the
    /// root-key config shape. A <c>[[...branch_lanes.&lt;domain&gt;.lanes]]</c>
    /// table-array form is also accepted. The previous singleton
    /// <c>[project.branch_lanes]</c> / <c>[branch_lanes]</c> spelling remains
    /// accepted, but is scoped only to the config's declared domain rather
    /// than becoming a host-wide registry.
    /// </summary>
    private static IReadOnlyDictionary<string, BranchLaneRegistry> ReadBranchLanes(
        TomlTable table,
        string contractName,
        string defaultDomain)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDomain);

        var empty = new Dictionary<string, BranchLaneRegistry>(StringComparer.Ordinal);

        if (!table.TryGetValue(CliRuntimeContracts.BranchLanesSectionName, out var raw)
            || raw is not TomlTable registryTable)
        {
            return empty;
        }

        // A direct default/revision/lanes key is the pre-repair singleton
        // shape. Keep it readable, but bind it to the declared domain so it
        // cannot accidentally serve every domain in a multi-domain host.
        if (LooksLikeRegistryTable(registryTable))
        {
            empty.Add(
                defaultDomain,
                ReadBranchLaneRegistry(registryTable, contractName));
            return empty;
        }

        foreach (var pair in registryTable)
        {
            if (pair.Value is not TomlTable domainTable)
            {
                throw new InvalidOperationException(
                    $"{contractName} '{CliRuntimeContracts.BranchLanesSectionName}' domain '{pair.Key}' must be a TOML table.");
            }

            if (empty.ContainsKey(pair.Key))
            {
                throw new InvalidOperationException(
                    $"{contractName} declares duplicate branch-lane domain '{pair.Key}'.");
            }

            empty.Add(
                pair.Key,
                ReadBranchLaneRegistry(domainTable, $"{contractName} domain '{pair.Key}'"));
        }

        if (empty.Count == 0)
        {
            throw new InvalidOperationException(
                $"{contractName} '{CliRuntimeContracts.BranchLanesSectionName}' must declare at least one domain registry.");
        }

        return empty;
    }

    private static bool LooksLikeRegistryTable(TomlTable table)
    {
        return table.ContainsKey(CliRuntimeContracts.BranchLanesDefaultLaneKey)
            || table.ContainsKey(CliRuntimeContracts.BranchLanesDefinitionRevisionKey)
            || table.ContainsKey(CliRuntimeContracts.BranchLanesRevisionKey)
            || table.ContainsKey(CliRuntimeContracts.BranchLanesLanesKey)
            || table.Values.OfType<TomlTable>().Any(LooksLikeLaneTable);
    }

    private static bool LooksLikeLaneTable(TomlTable table)
    {
        return table.ContainsKey(CliRuntimeContracts.BranchLaneStartBranchKey)
            || table.ContainsKey(CliRuntimeContracts.BranchLanePrBaseBranchKey)
            || table.ContainsKey(CliRuntimeContracts.BranchLaneLandingModeKey);
    }

    private static BranchLaneRegistry ReadBranchLaneRegistry(TomlTable registryTable, string contractName)
    {
        var defaultLane = TryGetOptionalString(registryTable, CliRuntimeContracts.BranchLanesDefaultLaneKey);
        var declaredRevision = TryGetOptionalString(registryTable, CliRuntimeContracts.BranchLanesDefinitionRevisionKey)
            ?? TryGetOptionalString(registryTable, CliRuntimeContracts.BranchLanesRevisionKey);
        var lanes = new Dictionary<string, BranchLaneDefinition>(StringComparer.Ordinal);

        if (registryTable.TryGetValue(CliRuntimeContracts.BranchLanesLanesKey, out var laneArrayRaw))
        {
            if (laneArrayRaw is not TomlTableArray laneArray)
            {
                throw new InvalidOperationException(
                    $"{contractName} '{CliRuntimeContracts.BranchLanesLanesKey}' must be an array of tables.");
            }

            foreach (var laneTable in laneArray)
            {
                var id = TryGetOptionalString(laneTable, CliRuntimeContracts.BranchLaneIdKey)
                    ?? TryGetOptionalString(laneTable, CliRuntimeContracts.BranchLaneNameKey);
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException(
                        $"{contractName} branch-lane entries must declare '{CliRuntimeContracts.BranchLaneIdKey}' or '{CliRuntimeContracts.BranchLaneNameKey}'.");
                }

                AddBranchLane(lanes, ReadBranchLane(laneTable, id.Trim()), contractName);
            }
        }

        foreach (var pair in registryTable)
        {
            if (pair.Key is CliRuntimeContracts.BranchLanesDefaultLaneKey
                or CliRuntimeContracts.BranchLanesDefinitionRevisionKey
                or CliRuntimeContracts.BranchLanesRevisionKey
                or CliRuntimeContracts.BranchLanesLanesKey)
            {
                continue;
            }

            if (pair.Value is TomlTable laneTable)
            {
                AddBranchLane(lanes, ReadBranchLane(laneTable, pair.Key), contractName);
            }
        }

        if (lanes.Count == 0)
        {
            throw new InvalidOperationException(
                $"{contractName} '{CliRuntimeContracts.BranchLanesSectionName}' must declare at least one named lane.");
        }

        if (!string.IsNullOrWhiteSpace(defaultLane) && !lanes.ContainsKey(defaultLane))
        {
            throw new InvalidOperationException(
                $"{contractName} default_lane '{defaultLane}' does not name a configured branch lane.");
        }

        return new BranchLaneRegistry
        {
            DefaultLane = defaultLane,
            DefinitionRevision = BranchLaneResolver.ComputeDefinitionRevision(lanes, declaredRevision),
            Lanes = lanes
        };
    }

    private static BranchLaneDefinition ReadBranchLane(TomlTable table, string id)
    {
        var startBranch = RequireBranchLaneString(table, CliRuntimeContracts.BranchLaneStartBranchKey, id);
        var prBaseBranch = RequireBranchLaneString(table, CliRuntimeContracts.BranchLanePrBaseBranchKey, id);
        var landingMode = RequireBranchLaneString(table, CliRuntimeContracts.BranchLaneLandingModeKey, id);

        return new BranchLaneDefinition
        {
            Id = id,
            StartBranch = startBranch,
            PrBaseBranch = prBaseBranch,
            LandingMode = landingMode
        };
    }

    private static string RequireBranchLaneString(TomlTable table, string key, string laneId)
    {
        var value = TryGetOptionalString(table, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Branch lane '{laneId}' must declare '{key}'.");
        }

        return value.Trim();
    }

    private static void AddBranchLane(
        IDictionary<string, BranchLaneDefinition> lanes,
        BranchLaneDefinition lane,
        string contractName)
    {
        if (!lanes.TryAdd(lane.Id, lane))
        {
            throw new InvalidOperationException(
                $"{contractName} declares duplicate branch lane '{lane.Id}'.");
        }
    }

    private static void RejectObsoleteWorkflowEngineKey(TomlTable table, string contractName)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        if (table.ContainsKey("workflow_engine"))
        {
            throw new InvalidOperationException(
                $"{contractName} must not contain obsolete key 'workflow_engine'.");
        }
    }

    private static RoleMappings ReadRoles(TomlTable rootTable)
    {
        ArgumentNullException.ThrowIfNull(rootTable);

        if (!rootTable.TryGetValue(CliRuntimeContracts.RolesSectionName, out var section)
            || section is not TomlTable rolesTable)
        {
            return new RoleMappings();
        }

        return new RoleMappings
        {
            Implement = TryGetOptionalString(rolesTable, CliRuntimeContracts.ImplementRoleKey)
                ?? CliRuntimeContracts.DefaultImplementRole,
            Review = TryGetOptionalString(rolesTable, CliRuntimeContracts.ReviewRoleKey)
                ?? CliRuntimeContracts.DefaultReviewRole,
            Interview = TryGetOptionalString(rolesTable, CliRuntimeContracts.InterviewRoleKey)
                ?? CliRuntimeContracts.DefaultInterviewRole,
            Clarify = TryGetOptionalString(rolesTable, CliRuntimeContracts.ClarifyRoleKey)
                ?? CliRuntimeContracts.DefaultClarifyRole
        };
    }

    private static SupervisionConfig ReadSupervision(TomlTable rootTable)
    {
        ArgumentNullException.ThrowIfNull(rootTable);

        if (!rootTable.TryGetValue(CliRuntimeContracts.SupervisionSectionName, out var section)
            || section is not TomlTable supervisionTable)
        {
            return new SupervisionConfig();
        }

        return new SupervisionConfig
        {
            ArtifactRoot = TryGetOptionalString(supervisionTable, CliRuntimeContracts.ArtifactRootKey)
                ?? CliRuntimeContracts.DefaultSupervisionArtifactRoot,
            StaleHeartbeatTimeoutMinutes = TryGetOptionalInt32(
                supervisionTable,
                CliRuntimeContracts.StaleHeartbeatTimeoutMinutesKey)
                ?? CliRuntimeContracts.DefaultSupervisionStaleHeartbeatTimeoutMinutes,
            RetryDelayMinutes = TryGetOptionalInt32(
                supervisionTable,
                CliRuntimeContracts.RetryDelayMinutesKey)
                ?? CliRuntimeContracts.DefaultSupervisionRetryDelayMinutes,
            RetryBudget = TryGetOptionalInt32(supervisionTable, CliRuntimeContracts.RetryBudgetKey)
                ?? CliRuntimeContracts.DefaultSupervisionRetryBudget
        };
    }

    private static RunConfig ReadRun(TomlTable rootTable)
    {
        ArgumentNullException.ThrowIfNull(rootTable);

        if (!rootTable.TryGetValue(CliRuntimeContracts.RunSectionName, out var section)
            || section is not TomlTable runTable)
        {
            return new RunConfig();
        }

        var postFixWorktreeProgressPolicy = TryGetOptionalString(
                runTable,
                CliRuntimeContracts.PostFixWorktreeProgressPolicyKey)
            ?? CliRuntimeContracts.DefaultPostFixWorktreeProgressPolicy;
        ValidatePostFixWorktreeProgressPolicy(postFixWorktreeProgressPolicy);

        return new RunConfig
        {
            PostFixWorktreeProgressPolicy = postFixWorktreeProgressPolicy
        };
    }

    private static DirectRunConfig ReadDirectRun(TomlTable rootTable)
    {
        ArgumentNullException.ThrowIfNull(rootTable);

        if (!rootTable.TryGetValue(CliRuntimeContracts.DirectRunSectionName, out var section)
            || section is not TomlTable directRunTable)
        {
            return new DirectRunConfig();
        }

        return new DirectRunConfig
        {
            ArtifactRoot = TryGetOptionalString(directRunTable, CliRuntimeContracts.ArtifactRootKey)
                ?? CliRuntimeContracts.DefaultDirectRunArtifactRoot,
            Provider = TryGetOptionalString(directRunTable, CliRuntimeContracts.ProviderKey)
                ?? string.Empty,
            Model = TryGetOptionalString(directRunTable, CliRuntimeContracts.ModelKey)
                ?? CliRuntimeContracts.DefaultDirectRunModel,
            Transport = TryGetOptionalString(directRunTable, CliRuntimeContracts.TransportKey)
                ?? CliRuntimeContracts.DefaultDirectRunTransport,
            Command = TryGetOptionalString(directRunTable, CliRuntimeContracts.CommandKey)
                ?? string.Empty,
            Args = TryGetOptionalStringArray(directRunTable, CliRuntimeContracts.ArgsKey)
                ?? [],
            Implement = ReadDirectRunEntry(directRunTable, CliRuntimeContracts.ImplementRoleKey),
            Fix = ReadDirectRunEntry(directRunTable, "fix"),
            Review = ReadDirectRunEntry(directRunTable, CliRuntimeContracts.ReviewRoleKey)
        };
    }

    private static DirectRunEntryConfig ReadDirectRunEntry(TomlTable parentTable, string sectionKey)
    {
        ArgumentNullException.ThrowIfNull(parentTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

        if (!parentTable.TryGetValue(sectionKey, out var section)
            || section is not TomlTable entryTable)
        {
            return new DirectRunEntryConfig();
        }

        return new DirectRunEntryConfig
        {
            Provider = TryGetOptionalString(entryTable, CliRuntimeContracts.ProviderKey) ?? string.Empty,
            Model = TryGetOptionalString(entryTable, CliRuntimeContracts.ModelKey) ?? string.Empty,
            Transport = TryGetOptionalString(entryTable, CliRuntimeContracts.TransportKey) ?? string.Empty,
            Command = TryGetOptionalString(entryTable, CliRuntimeContracts.CommandKey) ?? string.Empty,
            Args = TryGetOptionalStringArray(entryTable, CliRuntimeContracts.ArgsKey) ?? []
        };
    }

    private static bool TryGetRequiredString(TomlTable table, string key, out string value)
    {
        value = string.Empty;

        if (!table.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        if (rawValue is not string textValue || string.IsNullOrWhiteSpace(textValue))
        {
            throw new InvalidOperationException(
                $"CLI config value '{key}' must be a non-empty string.");
        }

        value = textValue;
        return true;
    }

    private static string? TryGetOptionalString(TomlTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!table.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is not string textValue || string.IsNullOrWhiteSpace(textValue))
        {
            throw new InvalidOperationException(
                $"CLI config value '{key}' must be a non-empty string.");
        }

        return textValue;
    }

    /// <summary>
    /// G362: TOML loader helper for optional boolean keys. Used by
    /// the `same_repo_topology` flag. Throws when the key is present
    /// with a non-bool value rather than silently defaulting.
    /// </summary>
    private static bool? TryGetOptionalBool(TomlTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!table.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is bool boolValue)
        {
            return boolValue;
        }

        throw new InvalidOperationException(
            $"CLI config value '{key}' must be a boolean.");
    }

    private static int? TryGetOptionalInt32(TomlTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!table.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is long longValue)
        {
            if (longValue <= 0 || longValue > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"CLI config value '{key}' must be a positive integer.");
            }

            return (int)longValue;
        }

        throw new InvalidOperationException(
            $"CLI config value '{key}' must be a positive integer.");
    }

    private static IReadOnlyList<string>? TryGetOptionalStringArray(TomlTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!table.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        if (rawValue is not TomlArray arrayValue)
        {
            throw new InvalidOperationException(
                $"CLI config value '{key}' must be an array of non-empty strings.");
        }

        var values = new List<string>(arrayValue.Count);
        foreach (var item in arrayValue)
        {
            if (item is not string textValue || string.IsNullOrWhiteSpace(textValue))
            {
                throw new InvalidOperationException(
                    $"CLI config value '{key}' must be an array of non-empty strings.");
            }

            values.Add(textValue);
        }

        return values;
    }

    private static void ValidatePostFixWorktreeProgressPolicy(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);

        if (string.Equals(policy, CliRuntimeContracts.ConfirmPostFixWorktreeProgressPolicy, StringComparison.Ordinal)
            || string.Equals(policy, CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"CLI config value '{CliRuntimeContracts.PostFixWorktreeProgressPolicyKey}' must be " +
            $"'{CliRuntimeContracts.ConfirmPostFixWorktreeProgressPolicy}' or " +
            $"'{CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy}'.");
    }
}
