using IntentSystem.Drift.Models;
using IntentSystem.Drift.Serialization;

namespace IntentSystem.Drift.Tests;

public sealed class DriftClassificationSerializerTests
{
    [Fact]
    public void Serialize_GivenReport_WritesCurrentFieldShape()
    {
        var serialized = DriftClassificationSerializer.Serialize(
            new DriftClassificationReport
            {
                Items =
                [
                    new DriftClassificationItem
                    {
                        ExecutionUnit = "G9",
                        Classification = DriftClassification.AcceptedContractBreaking,
                        ChangedCanonicalRefs = ["intents/rules/intent-diff-and-corrective-issues.md"],
                        CorrectiveExecutionUnit = "G9-corrective"
                    }
                ]
            });

        Assert.Contains("\"execution_unit\": \"G9\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"classification\": \"accepted-contract-breaking\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"changed_canonical_refs\": [", serialized, StringComparison.Ordinal);
        Assert.Contains("\"corrective_execution_unit\": \"G9-corrective\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "items": [
            {
              "execution_unit": "G9",
              "changed_canonical_refs": []
            }
          ]
        }
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => DriftClassificationSerializer.Deserialize(json));

        Assert.Contains("classification", exception.Message, StringComparison.Ordinal);
    }
}
