using System.Text.Json;
using IntentSystem.Clarify.Models;
using IntentSystem.WorkerAdapter.Models;
using IntentSystem.WorkerAdapter.Serialization;
using IntentSystem.Workflow.Models;

namespace IntentSystem.WorkerAdapter.Tests;

public sealed class WorkerAdapterSerializerTests
{
    [Fact]
    public void SerializeRequest_GivenCompleteContract_ContainsAllRequiredFields()
    {
        var request = CreateRequest();

        var serialized = WorkerAdapterSerializer.SerializeRequest(request);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal(".intent-cli/workflows/C2.yaml", root.GetProperty("workflow_definition_ref").GetString());
        Assert.Equal("run-20260403-001", root.GetProperty("run_id").GetString());
        Assert.Equal("/worktrees/intent-system-c2", root.GetProperty("target_worktree").GetString());
        Assert.Equal("takt", root.GetProperty("runtime_env").GetProperty("engine").GetString());
        Assert.Equal("jsonl-file", root.GetProperty("event_sink").GetProperty("sink_type").GetString());
    }

    [Fact]
    public void SerializeRequest_GivenCompleteContract_DoesNotLeakQueueStateFields()
    {
        var request = CreateRequest();

        var serialized = WorkerAdapterSerializer.SerializeRequest(request);

        Assert.DoesNotContain("\"current_queue_state\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"queue_state\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"resume_reason\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeRequest_GivenCompleteJson_RestoresRuntimeAndEventSink()
    {
        var json = """
        {
          "workflow_definition_ref": ".intent-cli/workflows/C2.yaml",
          "run_id": "run-20260403-001",
          "target_worktree": "/worktrees/intent-system-c2",
          "runtime_env": {
            "engine": "takt",
            "arguments": ["run", ".intent-cli/workflows/C2.yaml"],
            "variables": {
              "CODEX_APPROVAL_MODE": "never"
            }
          },
          "event_sink": {
            "sink_type": "jsonl-file",
            "sink_ref": ".intent-cli/workflows/C2.run.json"
          }
        }
        """;

        var request = WorkerAdapterSerializer.DeserializeRequest(json);

        Assert.Equal(".intent-cli/workflows/C2.yaml", request.WorkflowDefinitionRef);
        Assert.Equal("run-20260403-001", request.RunId);
        Assert.Equal("/worktrees/intent-system-c2", request.TargetWorktree);
        Assert.Equal("takt", request.RuntimeEnv.Engine);
        Assert.Equal(["run", ".intent-cli/workflows/C2.yaml"], request.RuntimeEnv.Arguments);
        Assert.Equal("never", request.RuntimeEnv.Variables["CODEX_APPROVAL_MODE"]);
        Assert.Equal("jsonl-file", request.EventSink.SinkType);
        Assert.Equal(".intent-cli/workflows/C2.run.json", request.EventSink.SinkRef);
    }

    [Fact]
    public void DeserializeRequest_GivenMissingWorkflowDefinitionRef_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "run_id": "run-20260403-001",
          "target_worktree": "/worktrees/intent-system-c2",
          "runtime_env": {
            "engine": "takt",
            "arguments": []
          },
          "event_sink": {
            "sink_type": "jsonl-file",
            "sink_ref": ".intent-cli/workflows/C2.run.json"
          }
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => WorkerAdapterSerializer.DeserializeRequest(json));

        Assert.Contains("workflow_definition_ref", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeResult_GivenRepairInPlaceFailure_PreservesReviewRefsAndResultSummary()
    {
        var result = CreateReviewRejectedResult();

        var serialized = WorkerAdapterSerializer.SerializeResult(result);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("review-rejected", root.GetProperty("run_status").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("step_statuses").ValueKind);
        Assert.Equal("changes-requested", root.GetProperty("review_result").GetProperty("disposition").GetString());
        Assert.Equal(
            "https://github.com/J-Tech-Japan/intent-system/pull/24#discussion_r123",
            root.GetProperty("review_comment_refs")[0].GetString());
        Assert.Equal("Review requested one contract repair before rereview.", root.GetProperty("result_summary").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("run_log_refs").ValueKind);
    }

    [Fact]
    public void SerializeResult_GivenContract_DoesNotContainQueuePolicyFields()
    {
        var result = CreateReviewRejectedResult();

        var serialized = WorkerAdapterSerializer.SerializeResult(result);

        Assert.DoesNotContain("\"queue_state\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"queue_item\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"blocked_by\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeResult_GivenClarificationRequest_RestoresCanonicalClarificationArtifacts()
    {
        var json = """
        {
          "run_status": "clarification-requested",
          "step_statuses": [
            {
              "step": "implement",
              "status": "completed"
            },
            {
              "step": "clarify",
              "status": "blocked",
              "detail": "Need parent intent confirmation for event sink path."
            }
          ],
          "review_result": {
            "disposition": "clarification-requested",
            "reviewed_by": "reviewer"
          },
          "review_comment_refs": [],
          "clarification_requests": [
            {
              "artifact_kind": "clarification",
              "clarification_source": "review",
              "question_id": "clar-c2-1",
              "execution_unit": "C2",
              "question_text": "Which sink ref should be canonical for takt run events?",
              "reason": "Need a stable outward event log location before adapter execution.",
              "affected_intents": ["intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md"],
              "affected_execution_units": ["C2"],
              "blocking_or_nonblocking": "blocking",
              "clarification_return_path": "intents/intent-cli/specs/07-workflow-definition-and-takt-adapter.md",
              "status": "open",
              "created_at": "2026-04-03T09:00:00Z"
            }
          ],
          "result_summary": "Adapter paused for clarification before review completion.",
          "run_log_refs": [".intent-cli/workflows/C2.run.json"]
        }
        """;

        var result = WorkerAdapterSerializer.DeserializeResult(json);

        Assert.Equal(WorkerAdapterRunStatus.ClarificationRequested, result.RunStatus);
        Assert.Equal(2, result.StepStatuses.Count);
        Assert.Equal(WorkflowStepKind.Implement, result.StepStatuses[0].Step);
        Assert.Equal(WorkerAdapterStepState.Blocked, result.StepStatuses[1].Status);
        Assert.Equal(WorkerReviewDisposition.ClarificationRequested, result.ReviewResult.Disposition);
        var clarification = Assert.Single(result.ClarificationRequests);
        Assert.Equal("clar-c2-1", clarification.QuestionId);
        Assert.Equal(["C2"], clarification.AffectedExecutionUnits);
        Assert.Equal(".intent-cli/workflows/C2.run.json", Assert.Single(result.RunLogRefs));
    }

    [Fact]
    public void DeserializeResult_GivenMissingReviewCommentRefs_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "run_status": "succeeded",
          "step_statuses": [],
          "review_result": {
            "disposition": "approved"
          },
          "clarification_requests": [],
          "result_summary": "done",
          "run_log_refs": []
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => WorkerAdapterSerializer.DeserializeResult(json));

        Assert.Contains("review_comment_refs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeAndDeserializeResult_RoundTrips()
    {
        var result = CreateReviewRejectedResult();

        var serialized = WorkerAdapterSerializer.SerializeResult(result);
        var deserialized = WorkerAdapterSerializer.DeserializeResult(serialized);

        Assert.Equal(result.RunStatus, deserialized.RunStatus);
        Assert.Equal(result.StepStatuses.Count, deserialized.StepStatuses.Count);
        Assert.Equal(result.ReviewResult.Disposition, deserialized.ReviewResult.Disposition);
        Assert.Equal(result.ReviewCommentRefs, deserialized.ReviewCommentRefs);
        Assert.Equal(result.ResultSummary, deserialized.ResultSummary);
        Assert.Equal(result.RunLogRefs, deserialized.RunLogRefs);
    }

    private static WorkerAdapterRequest CreateRequest()
    {
        return new WorkerAdapterRequest
        {
            WorkflowDefinitionRef = ".intent-cli/workflows/C2.yaml",
            RunId = "run-20260403-001",
            TargetWorktree = "/worktrees/intent-system-c2",
            RuntimeEnv = new AdapterRuntimeEnvironment
            {
                Engine = "takt",
                Arguments = ["run", ".intent-cli/workflows/C2.yaml"],
                Variables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["CODEX_APPROVAL_MODE"] = "never",
                    ["DOTNET_ENVIRONMENT"] = "Development"
                }
            },
            EventSink = new AdapterEventSink
            {
                SinkType = "jsonl-file",
                SinkRef = ".intent-cli/workflows/C2.run.json"
            }
        };
    }

    private static WorkerAdapterResult CreateReviewRejectedResult()
    {
        return new WorkerAdapterResult
        {
            RunStatus = WorkerAdapterRunStatus.ReviewRejected,
            StepStatuses =
            [
                new WorkerAdapterStepStatus
                {
                    Step = WorkflowStepKind.Implement,
                    Status = WorkerAdapterStepState.Completed
                },
                new WorkerAdapterStepStatus
                {
                    Step = WorkflowStepKind.Review,
                    Status = WorkerAdapterStepState.Failed,
                    Detail = "Reviewer requested a contract repair."
                },
                new WorkerAdapterStepStatus
                {
                    Step = WorkflowStepKind.Fix,
                    Status = WorkerAdapterStepState.Pending
                }
            ],
            ReviewResult = new WorkerReviewResult
            {
                Disposition = WorkerReviewDisposition.ChangesRequested,
                ReviewedBy = "reviewer"
            },
            ReviewCommentRefs =
            [
                "https://github.com/J-Tech-Japan/intent-system/pull/24#discussion_r123"
            ],
            ClarificationRequests = [],
            ResultSummary = "Review requested one contract repair before rereview.",
            RunLogRefs =
            [
                ".intent-cli/workflows/C2.run.json"
            ]
        };
    }
}
