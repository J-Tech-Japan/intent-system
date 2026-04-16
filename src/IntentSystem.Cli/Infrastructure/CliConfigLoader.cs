using IntentSystem.Cli.Models;
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
                ParentIntentRepoRoot = parentIntentRepoRoot
            },
            Roles = roles,
            Supervision = supervision,
            Run = run,
            DirectRun = directRun
        };
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
