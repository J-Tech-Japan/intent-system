using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Supervisor.Tests;

public sealed class QueueStateSerializerTests
{
    [Fact]
    public void Deserialize_GivenQueueStateJson_RestoresSelectiveBlockPacketPathsAndLinkedIssue()
    {
        var json = """
        {
          "schema_version": "1",
          "updated_at": "2026-04-02T09:50:13Z",
          "items": [
            {
              "execution_unit": "A1",
              "title": "Projection schema contract",
              "state": "completed",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": ".takt/runs/20260402-095013-issue-7-b1-queue-json-and-json/context/task/order.md",
              "packet_paths": {
                "implementation": ".intent-cli/packets/A1/implementation.md",
                "review_context": ".intent-cli/packets/A1/review-context.md",
                "yaml": ".intent-cli/packets/A1/packet.yaml"
              },
              "linked_issue": {
                "repo": "J-Tech-Japan/intent-system",
                "number": 7,
                "url": "https://github.com/J-Tech-Japan/intent-system/issues/7"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            },
            {
              "execution_unit": "B1",
              "title": "Queue JSON and JSONL schema",
              "state": "queued",
              "dependencies": [
                "A1"
              ],
              "blocked_by": [
                "A1"
              ],
              "clarification_return_path": ".takt/runs/20260402-095013-issue-7-b1-queue-json-and-json/context/task/order.md",
              "packet_paths": {
                "implementation": ".intent-cli/packets/B1/implementation.md",
                "review_context": ".intent-cli/packets/B1/review-context.md",
                "yaml": ".intent-cli/packets/B1/packet.yaml"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            }
          ]
        }
        """;

        var queueState = QueueStateSerializer.Deserialize(json);

        Assert.Equal("1", queueState.SchemaVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-04-02T09:50:13Z"), queueState.UpdatedAt);
        Assert.Equal(2, queueState.Items.Count);

        var completedItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "A1");
        Assert.Equal(QueueItemState.Completed, completedItem.State);
        Assert.Empty(completedItem.Dependencies);
        Assert.Empty(completedItem.BlockedBy);
        Assert.Equal(".intent-cli/packets/A1/implementation.md", completedItem.PacketPaths.Implementation);
        Assert.Equal(".intent-cli/packets/A1/review-context.md", completedItem.PacketPaths.ReviewContext);
        Assert.Equal(".intent-cli/packets/A1/packet.yaml", completedItem.PacketPaths.Yaml);
        Assert.NotNull(completedItem.LinkedIssue);
        Assert.Equal("J-Tech-Japan/intent-system", completedItem.LinkedIssue!.Repo);
        Assert.Equal(7, completedItem.LinkedIssue.Number);
        Assert.Equal(
            "https://github.com/J-Tech-Japan/intent-system/issues/7",
            completedItem.LinkedIssue.Url);

        var queuedItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "B1");
        Assert.Equal(QueueItemState.Queued, queuedItem.State);
        Assert.Equal(["A1"], queuedItem.Dependencies);
        Assert.Equal(["A1"], queuedItem.BlockedBy);
        Assert.Null(queuedItem.LinkedIssue);
    }

    [Fact]
    public void DeserializeAndSerialize_GivenClarifyBlockedState_UsesKebabCaseEnumValue()
    {
        var json = """
        {
          "schema_version": "1",
          "updated_at": "2026-04-02T09:50:13Z",
          "items": [
            {
              "execution_unit": "B1",
              "title": "Queue JSON and JSONL schema",
              "state": "clarify-blocked",
              "dependencies": [
                "A1"
              ],
              "blocked_by": [
                "clarify-response"
              ],
              "clarification_return_path": ".intent-cli/clarify/B1.md",
              "packet_paths": {
                "implementation": ".intent-cli/packets/B1/implementation.md",
                "review_context": ".intent-cli/packets/B1/review-context.md",
                "yaml": ".intent-cli/packets/B1/packet.yaml"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            }
          ]
        }
        """;

        var queueState = QueueStateSerializer.Deserialize(json);
        var serialized = QueueStateSerializer.Serialize(queueState);

        var item = Assert.Single(queueState.Items);
        Assert.Equal(QueueItemState.ClarifyBlocked, item.State);
        Assert.Contains("\"state\": \"clarify-blocked\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_GivenQueueState_UsesSnakeCasePropertiesAndIndentedOutput()
    {
        var queueState = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-02T09:50:13Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "B1",
                    Title = "Queue JSON and JSONL schema",
                    State = QueueItemState.Review,
                    Dependencies = ["A1"],
                    BlockedBy = [],
                    ClarificationReturnPath = ".intent-cli/clarify/B1.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/packets/B1/implementation.md",
                        ReviewContext = ".intent-cli/packets/B1/review-context.md",
                        Yaml = ".intent-cli/packets/B1/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };

        var serialized = QueueStateSerializer.Serialize(queueState);
        using var document = JsonDocument.Parse(serialized);

        Assert.True(document.RootElement.TryGetProperty("schema_version", out _));
        Assert.True(document.RootElement.TryGetProperty("updated_at", out _));
        Assert.Contains("\"packet_paths\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schemaVersion\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"packetPaths\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\n  \"updated_at\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenLinkedIssueWithNullNumberAndUrl_TolerantlyReturnsNullableFields()
    {
        // G176: parent-host queue-state can carry pre-issue rows whose linked_issue.number / url
        // are still null. The submit/seed/numbering pipeline must not crash on the deserialize
        // boundary just because one row is mid-publish.
        var json = """
        {
          "schema_version": "1",
          "updated_at": "2026-04-26T05:00:00Z",
          "items": [
            {
              "execution_unit": "TOY-CALC-V0-11",
              "title": "Submitted unit",
              "state": "active",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/TOY-CALC-V0-11/implementation.md",
                "review_context": ".intent-cli/issues/TOY-CALC-V0-11/review-context.md",
                "yaml": ".intent-cli/issues/TOY-CALC-V0-11/packet.yaml"
              },
              "linked_issue": {
                "repo": "tomohisa/toy-calc-sample",
                "number": 24,
                "url": "https://github.com/tomohisa/toy-calc-sample/issues/24"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            },
            {
              "execution_unit": "SKS-G54",
              "title": "Pre-issue row mid-publish",
              "state": "queued",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/SKS-G54/implementation.md",
                "review_context": ".intent-cli/issues/SKS-G54/review-context.md",
                "yaml": ".intent-cli/issues/SKS-G54/packet.yaml"
              },
              "linked_issue": {
                "repo": "J-Tech-Japan/intent-system",
                "number": null,
                "url": null
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            }
          ]
        }
        """;

        var queueState = QueueStateSerializer.Deserialize(json);

        Assert.Equal(2, queueState.Items.Count);

        var resolvedItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "TOY-CALC-V0-11");
        Assert.NotNull(resolvedItem.LinkedIssue);
        Assert.Equal(24, resolvedItem.LinkedIssue!.Number);
        Assert.Equal(
            "https://github.com/tomohisa/toy-calc-sample/issues/24",
            resolvedItem.LinkedIssue.Url);

        var preIssueItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "SKS-G54");
        Assert.NotNull(preIssueItem.LinkedIssue);
        Assert.Equal("J-Tech-Japan/intent-system", preIssueItem.LinkedIssue!.Repo);
        Assert.Null(preIssueItem.LinkedIssue.Number);
        Assert.Null(preIssueItem.LinkedIssue.Url);
    }

    [Fact]
    public void Deserialize_GivenObjectShapedLinkedPr_TolerantlyExtractsUrl()
    {
        // G177: parent-host queue-state may carry rows whose linked_pr is the
        // structured GitHub PR reference shape { repo, number, url } — same family
        // of canonical shape as linked_issue. The deserialize boundary must accept
        // both the legacy bare-string form and the structured object form so that
        // run submit does not crash with JsonException at $.items[*].linked_pr.
        var json = """
        {
          "schema_version": "1",
          "updated_at": "2026-04-27T20:56:58Z",
          "items": [
            {
              "execution_unit": "SKS-A1",
              "title": "SKS-A1 Bootstrap Management Service Control Plane",
              "state": "completed",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/SKS-A1/implementation.md",
                "review_context": ".intent-cli/issues/SKS-A1/review-context.md",
                "yaml": ".intent-cli/issues/SKS-A1/packet.yaml"
              },
              "linked_issue": {
                "repo": "J-Tech-Japan/SekibanAsAService",
                "number": 85,
                "url": "https://github.com/J-Tech-Japan/SekibanAsAService/issues/85"
              },
              "linked_pr": {
                "repo": "J-Tech-Japan/SekibanAsAService",
                "number": 86,
                "url": "https://github.com/J-Tech-Japan/SekibanAsAService/pull/86"
              },
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            },
            {
              "execution_unit": "TOY-CALC-V0-11",
              "title": "Legacy bare-string linked_pr",
              "state": "completed",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/TOY-CALC-V0-11/implementation.md",
                "review_context": ".intent-cli/issues/TOY-CALC-V0-11/review-context.md",
                "yaml": ".intent-cli/issues/TOY-CALC-V0-11/packet.yaml"
              },
              "linked_pr": "https://github.com/tomohisa/toy-calc-sample/pull/25",
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            },
            {
              "execution_unit": "SKS-G54",
              "title": "Pre-PR row with null linked_pr",
              "state": "queued",
              "dependencies": [],
              "blocked_by": [],
              "clarification_return_path": "intents/intent-cli/clarifications/open.md",
              "packet_paths": {
                "implementation": ".intent-cli/issues/SKS-G54/implementation.md",
                "review_context": ".intent-cli/issues/SKS-G54/review-context.md",
                "yaml": ".intent-cli/issues/SKS-G54/packet.yaml"
              },
              "linked_pr": null,
              "worker_role": "coder",
              "review_role": "reviewer",
              "priority": "high"
            }
          ]
        }
        """;

        var queueState = QueueStateSerializer.Deserialize(json);

        Assert.Equal(3, queueState.Items.Count);

        var objectShapedItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "SKS-A1");
        Assert.Equal(
            "https://github.com/J-Tech-Japan/SekibanAsAService/pull/86",
            objectShapedItem.LinkedPr);

        var legacyStringItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "TOY-CALC-V0-11");
        Assert.Equal(
            "https://github.com/tomohisa/toy-calc-sample/pull/25",
            legacyStringItem.LinkedPr);

        var nullPrItem = Assert.Single(queueState.Items, item => item.ExecutionUnit == "SKS-G54");
        Assert.Null(nullPrItem.LinkedPr);
    }
}
