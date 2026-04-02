using System.Text.Json;
using IntentSystem.ConceptIntake.Models;
using IntentSystem.ConceptIntake.Serialization;

namespace IntentSystem.ConceptIntake.Tests;

public sealed class ConceptIntakeSerializerTests
{
    [Fact]
    public void SerializePacket_GivenCompletePacket_ContainsAllRequiredFields()
    {
        var packet = CreatePacket();

        var serialized = ConceptIntakeSerializer.SerializePacket(packet);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal("auth", root.GetProperty("domain_slug").GetString());
        Assert.Equal("feature-request", root.GetProperty("concept_source").GetString());
        Assert.Equal("Add OAuth2 provider support.", root.GetProperty("concept_text").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("upstream_paths").ValueKind);
        Assert.Equal("Enable third-party authentication.", root.GetProperty("initial_goal").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("constraints").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("known_unknowns").ValueKind);
    }

    [Fact]
    public void SerializePacket_GivenCompletePacket_UsesSnakeCaseProperties()
    {
        var packet = CreatePacket();

        var serialized = ConceptIntakeSerializer.SerializePacket(packet);

        Assert.DoesNotContain("\"domainSlug\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"conceptSource\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"conceptText\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"upstreamPaths\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"initialGoal\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"knownUnknowns\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializePacket_GivenCompleteJson_RestoresAllFields()
    {
        var json = """
        {
          "domain_slug": "auth",
          "concept_source": "feature-request",
          "concept_text": "Add OAuth2 provider support.",
          "upstream_paths": ["intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"],
          "initial_goal": "Enable third-party authentication.",
          "constraints": ["Must not break existing session flow"],
          "known_unknowns": ["Which OAuth providers to support?"]
        }
        """;

        var packet = ConceptIntakeSerializer.DeserializePacket(json);

        Assert.Equal("auth", packet.DomainSlug);
        Assert.Equal("feature-request", packet.ConceptSource);
        Assert.Equal("Add OAuth2 provider support.", packet.ConceptText);
        Assert.Single(packet.UpstreamPaths);
        Assert.Equal("Enable third-party authentication.", packet.InitialGoal);
        Assert.Single(packet.Constraints);
        Assert.Single(packet.KnownUnknowns);
    }

    [Fact]
    public void DeserializePacket_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "domain_slug": "auth",
          "concept_source": "feature-request"
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConceptIntakeSerializer.DeserializePacket(json));

        Assert.Contains("required field", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializePacketAndDeserializePacket_RoundTrips()
    {
        var packet = CreatePacket();

        var serialized = ConceptIntakeSerializer.SerializePacket(packet);
        var deserialized = ConceptIntakeSerializer.DeserializePacket(serialized);

        Assert.Equal(packet.DomainSlug, deserialized.DomainSlug);
        Assert.Equal(packet.ConceptSource, deserialized.ConceptSource);
        Assert.Equal(packet.ConceptText, deserialized.ConceptText);
        Assert.Equal(packet.UpstreamPaths, deserialized.UpstreamPaths);
        Assert.Equal(packet.InitialGoal, deserialized.InitialGoal);
        Assert.Equal(packet.Constraints, deserialized.Constraints);
        Assert.Equal(packet.KnownUnknowns, deserialized.KnownUnknowns);
    }

    [Fact]
    public void SerializeOutput_GivenCompleteOutput_ContainsAllRequiredFields()
    {
        var output = CreateOutput();

        var serialized = ConceptIntakeSerializer.SerializeOutput(output);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.GetProperty("intent_draft_paths").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("interview_questions").ValueKind);
        Assert.Equal("intents/rules/issue-template-and-review-context.md",
            root.GetProperty("clarification_return_path").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("recommended_updates").ValueKind);
    }

    [Fact]
    public void SerializeOutput_GivenInterviewQuestions_ContainsAllRequiredQuestionFields()
    {
        var output = CreateOutput();

        var serialized = ConceptIntakeSerializer.SerializeOutput(output);
        using var document = JsonDocument.Parse(serialized);
        var question = document.RootElement
            .GetProperty("interview_questions")[0];

        Assert.Equal("iq-1", question.GetProperty("question_id").GetString());
        Assert.Equal("Which OAuth providers should be supported?",
            question.GetProperty("question_text").GetString());
        Assert.Equal("Provider selection impacts architecture.",
            question.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Array, question.GetProperty("affects").ValueKind);
        Assert.Equal("blocking", question.GetProperty("blocking_or_nonblocking").GetString());
    }

    [Fact]
    public void DeserializeOutput_GivenCompleteJson_RestoresAllFields()
    {
        var json = """
        {
          "intent_draft_paths": ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
          "interview_questions": [
            {
              "question_id": "iq-1",
              "question_text": "Which OAuth providers should be supported?",
              "reason": "Provider selection impacts architecture.",
              "affects": ["auth-oauth2"],
              "blocking_or_nonblocking": "blocking"
            }
          ],
          "clarification_return_path": "intents/rules/issue-template-and-review-context.md",
          "recommended_updates": ["Update auth strategy document"]
        }
        """;

        var output = ConceptIntakeSerializer.DeserializeOutput(json);

        Assert.Single(output.IntentDraftPaths);
        Assert.Single(output.InterviewQuestions);
        Assert.Equal("iq-1", output.InterviewQuestions[0].QuestionId);
        Assert.Equal("blocking", output.InterviewQuestions[0].BlockingOrNonblocking);
        Assert.Equal("intents/rules/issue-template-and-review-context.md", output.ClarificationReturnPath);
        Assert.Single(output.RecommendedUpdates);
    }

    [Fact]
    public void DeserializeOutput_GivenMissingRequiredOutputField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "intent_draft_paths": []
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConceptIntakeSerializer.DeserializeOutput(json));

        Assert.Contains("required field", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeOutput_GivenInterviewQuestionMissingRequiredField_ThrowsInvalidOperationException()
    {
        var json = """
        {
          "intent_draft_paths": [],
          "interview_questions": [
            {
              "question_id": "iq-1",
              "question_text": "Missing reason and affects"
            }
          ],
          "clarification_return_path": "path.md",
          "recommended_updates": []
        }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConceptIntakeSerializer.DeserializeOutput(json));

        Assert.Contains("Interview question at index 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeOutputAndDeserializeOutput_RoundTrips()
    {
        var output = CreateOutput();

        var serialized = ConceptIntakeSerializer.SerializeOutput(output);
        var deserialized = ConceptIntakeSerializer.DeserializeOutput(serialized);

        Assert.Equal(output.IntentDraftPaths, deserialized.IntentDraftPaths);
        Assert.Equal(output.InterviewQuestions.Count, deserialized.InterviewQuestions.Count);
        Assert.Equal(output.InterviewQuestions[0].QuestionId, deserialized.InterviewQuestions[0].QuestionId);
        Assert.Equal(output.InterviewQuestions[0].BlockingOrNonblocking, deserialized.InterviewQuestions[0].BlockingOrNonblocking);
        Assert.Equal(output.ClarificationReturnPath, deserialized.ClarificationReturnPath);
        Assert.Equal(output.RecommendedUpdates, deserialized.RecommendedUpdates);
    }

    private static ConceptIntakePacket CreatePacket()
    {
        return new ConceptIntakePacket
        {
            DomainSlug = "auth",
            ConceptSource = "feature-request",
            ConceptText = "Add OAuth2 provider support.",
            UpstreamPaths = ["intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"],
            InitialGoal = "Enable third-party authentication.",
            Constraints = ["Must not break existing session flow"],
            KnownUnknowns = ["Which OAuth providers to support?"]
        };
    }

    private static ConceptIntakeOutput CreateOutput()
    {
        return new ConceptIntakeOutput
        {
            IntentDraftPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            InterviewQuestions =
            [
                new InterviewQuestion
                {
                    QuestionId = "iq-1",
                    QuestionText = "Which OAuth providers should be supported?",
                    Reason = "Provider selection impacts architecture.",
                    Affects = ["auth-oauth2"],
                    BlockingOrNonblocking = "blocking"
                }
            ],
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            RecommendedUpdates = ["Update auth strategy document"]
        };
    }
}
