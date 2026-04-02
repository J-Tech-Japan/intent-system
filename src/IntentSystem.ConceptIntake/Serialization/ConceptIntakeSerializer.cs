using System.Text.Json;
using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.ConceptIntake.Serialization;

public static class ConceptIntakeSerializer
{
    public static string SerializePacket(ConceptIntakePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return JsonSerializer.Serialize(packet, ConceptIntakeJsonOptions.Indented);
    }

    public static ConceptIntakePacket DeserializePacket(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredPacketFields(document.RootElement);

        return JsonSerializer.Deserialize<ConceptIntakePacket>(json, ConceptIntakeJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Concept intake packet payload deserialized to null.");
    }

    public static string SerializeOutput(ConceptIntakeOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ValidateInterviewQuestions(output.InterviewQuestions);
        return JsonSerializer.Serialize(output, ConceptIntakeJsonOptions.Indented);
    }

    public static ConceptIntakeOutput DeserializeOutput(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        ValidateRequiredOutputFields(document.RootElement);
        ValidateInterviewQuestionFields(document.RootElement);

        var output = JsonSerializer.Deserialize<ConceptIntakeOutput>(json, ConceptIntakeJsonOptions.Compact)
            ?? throw new InvalidOperationException(
                "Concept intake output payload deserialized to null.");

        ValidateInterviewQuestions(output.InterviewQuestions);
        return output;
    }

    private static readonly string[] RequiredPacketFields =
    [
        "domain_slug",
        "concept_source",
        "concept_text",
        "upstream_paths",
        "initial_goal",
        "constraints",
        "known_unknowns"
    ];

    private static readonly string[] RequiredOutputFields =
    [
        "intent_draft_paths",
        "interview_questions",
        "clarification_return_path",
        "recommended_updates"
    ];

    private static readonly string[] RequiredInterviewQuestionFields =
    [
        "question_id",
        "question_text",
        "reason",
        "affects",
        "blocking_or_nonblocking"
    ];

    private static void ValidateRequiredPacketFields(JsonElement element)
    {
        foreach (var field in RequiredPacketFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"Concept intake packet must contain required field '{field}'.");
            }
        }
    }

    private static void ValidateRequiredOutputFields(JsonElement element)
    {
        foreach (var field in RequiredOutputFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                throw new InvalidOperationException(
                    $"Concept intake output must contain required field '{field}'.");
            }
        }
    }

    private static void ValidateInterviewQuestionFields(JsonElement element)
    {
        if (!element.TryGetProperty("interview_questions", out var questionsElement))
        {
            return;
        }

        if (questionsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var question in questionsElement.EnumerateArray())
        {
            foreach (var field in RequiredInterviewQuestionFields)
            {
                if (!question.TryGetProperty(field, out _))
                {
                    throw new InvalidOperationException(
                        $"Interview question at index {index} must contain required field '{field}'.");
                }
            }

            index++;
        }
    }

    private static void ValidateInterviewQuestions(IReadOnlyList<InterviewQuestion> questions)
    {
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            if (string.IsNullOrWhiteSpace(q.QuestionId))
            {
                throw new InvalidOperationException(
                    $"Interview question at index {i} must have a non-empty question_id.");
            }

            if (string.IsNullOrWhiteSpace(q.QuestionText))
            {
                throw new InvalidOperationException(
                    $"Interview question at index {i} must have a non-empty question_text.");
            }
        }
    }
}
