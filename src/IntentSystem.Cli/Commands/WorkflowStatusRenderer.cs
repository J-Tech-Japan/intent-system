using IntentSystem.WorkerAdapter.Models;
using IntentSystem.Workflow.Models;

namespace IntentSystem.Cli.Commands;

internal static class WorkflowStatusRenderer
{
    public static void Write(TextWriter writer, WorkflowDefinition definition, WorkerAdapterResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(result);

        var statusesByStep = BuildStatusesByStep(definition, result);

        writer.WriteLine($"Execution unit: {definition.ExecutionUnit}");
        writer.WriteLine($"Run status: {result.RunStatus}");
        writer.WriteLine($"Result summary: {result.ResultSummary}");
        writer.WriteLine($"Review disposition: {result.ReviewResult.Disposition}");
        writer.WriteLine($"Reviewed by: {FormatValue(result.ReviewResult.ReviewedBy)}");
        writer.WriteLine($"Review comment refs: {FormatList(result.ReviewCommentRefs)}");
        writer.WriteLine($"Run log refs: {FormatList(result.RunLogRefs)}");
        writer.WriteLine("Workflow steps:");

        foreach (var step in definition.Steps)
        {
            var status = statusesByStep[step.Kind];
            writer.WriteLine(
                $"- {step.Kind} | role={step.Role} | status={status.Status} | detail={FormatValue(status.Detail)}");
        }

        writer.WriteLine("Clarification requests:");
        if (result.ClarificationRequests.Count == 0)
        {
            writer.WriteLine("- none");
            return;
        }

        foreach (var clarification in result.ClarificationRequests)
        {
            writer.WriteLine(
                $"- {clarification.QuestionId} | execution_unit={clarification.ExecutionUnit} | status={clarification.Status} | blocking={clarification.BlockingOrNonblocking} | return={clarification.ClarificationReturnPath} | reason={clarification.Reason}");
        }
    }

    private static Dictionary<WorkflowStepKind, WorkerAdapterStepStatus> BuildStatusesByStep(
        WorkflowDefinition definition,
        WorkerAdapterResult result)
    {
        var workflowSteps = definition.Steps
            .Select(step => step.Kind)
            .ToHashSet();
        var statusesByStep = new Dictionary<WorkflowStepKind, WorkerAdapterStepStatus>();

        foreach (var stepStatus in result.StepStatuses)
        {
            if (!workflowSteps.Contains(stepStatus.Step))
            {
                throw new InvalidOperationException(
                    $"Workflow run artifact contained status for unknown step '{stepStatus.Step}'.");
            }

            if (!statusesByStep.TryAdd(stepStatus.Step, stepStatus))
            {
                throw new InvalidOperationException(
                    $"Workflow run artifact contained duplicate status for step '{stepStatus.Step}'.");
            }
        }

        foreach (var step in definition.Steps)
        {
            if (!statusesByStep.ContainsKey(step.Kind))
            {
                throw new InvalidOperationException(
                    $"Workflow run artifact did not contain status for step '{step.Kind}'.");
            }
        }

        return statusesByStep;
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "-"
            : string.Join(", ", values);
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value;
    }
}
