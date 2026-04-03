using System.Text.Json;
using IntentSystem.DomainBinding.Models;
using IntentSystem.DomainBinding.Serialization;

namespace IntentSystem.DomainBinding.Tests;

public sealed class DomainBindingSerializerTests
{
    [Fact]
    public void SerializeSource_GivenCompleteContract_ContainsAllRequiredFields()
    {
        var source = CreateSource();

        var serialized = DomainBindingSerializer.SerializeSource(source);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("issue-ready-sub-slice", root.GetProperty("source_kind").GetString());
        Assert.Equal("backend-first", root.GetProperty("dogfooding_track").GetString());
        Assert.Equal("F1", root.GetProperty("execution_unit").GetString());
        Assert.Equal("Create a projection-ready binding for the backend execution slice.", root.GetProperty("goal").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("dependencies").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("embedded_canonical_summary").ValueKind);
    }

    [Fact]
    public void SerializeSource_GivenCompleteContract_DoesNotExposePrivatePathOrUrlFields()
    {
        var source = CreateSource();

        var serialized = DomainBindingSerializer.SerializeSource(source);

        Assert.DoesNotContain("\"source_url\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"source_path\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"private_repo\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeSource_GivenCompleteJson_RestoresAllFields()
    {
        var json = """
        {
          "source_kind": "issue-ready-sub-slice",
          "dogfooding_track": "backend-first",
          "execution_unit": "F1",
          "goal": "Create a projection-ready binding for the backend execution slice.",
          "target_repo": "J-Tech-Japan/intent-system",
          "target_path": ".",
          "target_part": "domain binding",
          "dependencies": ["A2", "B2"],
          "success_signal": "backend sub-slice can be reconstructed as projection-ready input",
          "review_mode": "manual-review",
          "completion_action": "open-pr",
          "landing_policy": "squash",
          "embedded_canonical_summary": "Backend execution slice summary embedded for child-repo contract tests."
        }
        """;

        var source = DomainBindingSerializer.DeserializeSource(json);

        Assert.Equal(DomainExecutionSourceKind.IssueReadySubSlice, source.SourceKind);
        Assert.Equal(DogfoodingTrack.BackendFirst, source.DogfoodingTrack);
        Assert.Equal("F1", source.ExecutionUnit);
        Assert.Equal(["A2", "B2"], source.Dependencies);
        Assert.Equal("squash", source.LandingPolicy);
        Assert.Equal(
            "Backend execution slice summary embedded for child-repo contract tests.",
            source.EmbeddedCanonicalSummary);
    }

    [Fact]
    public void DeserializeSource_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "source_kind": "issue-ready-sub-slice",
          "execution_unit": "F1"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => DomainBindingSerializer.DeserializeSource(json));

        Assert.Contains("required field", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeProjectionReadySlice_GivenCompleteContract_ContainsAllGenericProjectionFields()
    {
        var projectionReady = CreateProjectionReadySlice();

        var serialized = DomainBindingSerializer.SerializeProjectionReadySlice(projectionReady);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("F1", root.GetProperty("execution_unit").GetString());
        Assert.Equal("domain binding", root.GetProperty("target_part").GetString());
        Assert.Equal("manual-review", root.GetProperty("review_mode").GetString());
        Assert.Equal("open-pr", root.GetProperty("completion_action").GetString());
        Assert.Equal("backend-first", root.GetProperty("dogfooding_track").GetString());
    }

    [Fact]
    public void SerializeProjectionReadySlice_GivenCompleteContract_DoesNotContainRendererQueueOrWorkflowFields()
    {
        var projectionReady = CreateProjectionReadySlice();

        var serialized = DomainBindingSerializer.SerializeProjectionReadySlice(projectionReady);

        Assert.DoesNotContain("\"packet_paths\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"queue_state\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"run_status\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeProjectionReadySlice_GivenCompleteJson_RestoresAllFields()
    {
        var json = """
        {
          "execution_unit": "F1",
          "goal": "Create a projection-ready binding for the backend execution slice.",
          "target_repo": "J-Tech-Japan/intent-system",
          "target_path": ".",
          "target_part": "domain binding",
          "dependencies": ["A2", "B2"],
          "success_signal": "backend sub-slice can be reconstructed as projection-ready input",
          "review_mode": "manual-review",
          "completion_action": "open-pr",
          "landing_policy": "squash",
          "dogfooding_track": "backend-first",
          "embedded_canonical_summary": "Backend execution slice summary embedded for child-repo contract tests."
        }
        """;

        var projectionReady = DomainBindingSerializer.DeserializeProjectionReadySlice(json);

        Assert.Equal("F1", projectionReady.ExecutionUnit);
        Assert.Equal(["A2", "B2"], projectionReady.Dependencies);
        Assert.Equal(DogfoodingTrack.BackendFirst, projectionReady.DogfoodingTrack);
        Assert.Equal("manual-review", projectionReady.ReviewMode);
    }

    [Fact]
    public void SerializeSourceAndDeserializeSource_RoundTrips()
    {
        var source = CreateSource();

        var serialized = DomainBindingSerializer.SerializeSource(source);
        var deserialized = DomainBindingSerializer.DeserializeSource(serialized);

        Assert.Equal(source.SourceKind, deserialized.SourceKind);
        Assert.Equal(source.DogfoodingTrack, deserialized.DogfoodingTrack);
        Assert.Equal(source.ExecutionUnit, deserialized.ExecutionUnit);
        Assert.Equal(source.Dependencies, deserialized.Dependencies);
        Assert.Equal(source.EmbeddedCanonicalSummary, deserialized.EmbeddedCanonicalSummary);
    }

    private static DomainExecutionSource CreateSource()
    {
        return new DomainExecutionSource
        {
            SourceKind = DomainExecutionSourceKind.IssueReadySubSlice,
            DogfoodingTrack = DogfoodingTrack.BackendFirst,
            ExecutionUnit = "F1",
            Goal = "Create a projection-ready binding for the backend execution slice.",
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "domain binding",
            Dependencies = ["A2", "B2"],
            SuccessSignal = "backend sub-slice can be reconstructed as projection-ready input",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr",
            LandingPolicy = "squash",
            EmbeddedCanonicalSummary = "Backend execution slice summary embedded for child-repo contract tests."
        };
    }

    private static ProjectionReadySlice CreateProjectionReadySlice()
    {
        return new ProjectionReadySlice
        {
            ExecutionUnit = "F1",
            Goal = "Create a projection-ready binding for the backend execution slice.",
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "domain binding",
            Dependencies = ["A2", "B2"],
            SuccessSignal = "backend sub-slice can be reconstructed as projection-ready input",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr",
            LandingPolicy = "squash",
            DogfoodingTrack = DogfoodingTrack.BackendFirst,
            EmbeddedCanonicalSummary = "Backend execution slice summary embedded for child-repo contract tests."
        };
    }
}
