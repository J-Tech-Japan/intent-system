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
}
