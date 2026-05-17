using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Output renderer for the read-only <c>automation summary</c> command (G186).
/// </summary>
internal static class AutomationSummaryRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static void WriteText(TextWriter writer, AutomationSummaryResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"# Automation summary for {result.Domain}");
        writer.WriteLine();

        writer.WriteLine("## Base branch policy");
        writer.WriteLine($"- effective_base_branch_policy: {result.EffectiveBaseBranchPolicy}");
        writer.WriteLine($"- implementation_base_branch: {result.ImplementationBaseBranch}");
        // G362: same-repo topology branch contract — only rendered
        // when the host opted into the same-repo gates so generic
        // host repos keep the pre-G362 output unchanged.
        if (result.SameRepoTopology
            || !string.IsNullOrWhiteSpace(result.MetadataSourceBranch)
            || !string.IsNullOrWhiteSpace(result.MetadataWriteBranch))
        {
            writer.WriteLine($"- same_repo_topology: {(result.SameRepoTopology ? "true" : "false")}");
            writer.WriteLine($"- metadata_source_branch: {(string.IsNullOrWhiteSpace(result.MetadataSourceBranch) ? "(unset)" : result.MetadataSourceBranch)}");
            writer.WriteLine($"- metadata_write_branch: {(string.IsNullOrWhiteSpace(result.MetadataWriteBranch) ? "(unset)" : result.MetadataWriteBranch)}");
        }
        writer.WriteLine();

        writer.WriteLine("## Bindings");
        writer.WriteLine($"- repo: {FormatNullable(result.Repo)}");
        writer.WriteLine($"- submodule_path: {FormatNullable(result.SubmodulePath)}");
        writer.WriteLine($"- queue_state_path: {FormatNullable(result.QueueStatePath)}");
        writer.WriteLine($"- runs_log_path: {FormatNullable(result.RunsLogPath)}");
        writer.WriteLine($"- packet_root: {FormatNullable(result.PacketRoot)}");
        writer.WriteLine($"- execution_unit_regex: {FormatNullable(result.ExecutionUnitRegex)}");
        writer.WriteLine();

        writer.WriteLine("## Issue workflow labels");
        WriteBulletList(writer, result.IssueWorkflowLabels);
        writer.WriteLine();

        writer.WriteLine("## PR workflow labels");
        WriteBulletList(writer, result.PrWorkflowLabels);
        writer.WriteLine();

        writer.WriteLine("## Host loop");
        WriteBulletList(writer, result.HostLoopResponsibilities);
        writer.WriteLine();

        writer.WriteLine("## Host PR transition commands");
        WriteBulletList(writer, result.HostPrTransitionCommands);
        writer.WriteLine();

        writer.WriteLine("## Automation command capabilities");
        writer.WriteLine($"- schema_version: {result.AutomationCapabilitySchemaVersion}");
        writer.WriteLine($"- surface_version: {result.AutomationCommandSurfaceVersion}");
        foreach (var capability in result.AutomationCommandCapabilities)
        {
            var transition = string.IsNullOrEmpty(capability.Transition) ? "(none)" : capability.Transition;
            writer.WriteLine($"- {capability.Capability}: command={capability.Command}; target={capability.TargetKind}; transition={transition}; write_supported={capability.WriteSupported.ToString().ToLowerInvariant()}");
        }
        writer.WriteLine();

        writer.WriteLine("## Child loop");
        WriteBulletList(writer, result.ChildLoopResponsibilities);
        writer.WriteLine();

        writer.WriteLine("## Publish boundary");
        writer.WriteLine($"- {result.PublishBoundaryGuidance}");
        writer.WriteLine();

        writer.WriteLine("## WIP cap");
        writer.WriteLine($"- {result.WipCapGuidance}");
        writer.WriteLine();

        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    public static void WriteJson(TextWriter writer, AutomationSummaryResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
    }

    private static void WriteBulletList(TextWriter writer, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            writer.WriteLine("- (none)");
            return;
        }

        foreach (var item in items)
        {
            writer.WriteLine($"- {item}");
        }
    }

    private static string FormatNullable(string? value)
    {
        return string.IsNullOrEmpty(value) ? "(none)" : value;
    }
}
