using Tomlyn;
using Tomlyn.Model;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G302: TOML serializer / deserializer for
/// <see cref="StructuredClarification"/> artifacts. Uses the existing
/// Tomlyn dependency so the format stays consistent with
/// <c>.intent-cli/config.toml</c> and <c>host-binding.toml</c>.
///
/// Round-trip is intentionally lossy on whitespace and comments so the
/// writer produces a canonical layout: scalars, then options as
/// <c>[[options]]</c> tables, then the optional <c>[answer]</c> table.
/// </summary>
internal static class StructuredClarificationToml
{
    public static StructuredClarification Deserialize(string toml, string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toml);

        var raw = TomlSerializer.Deserialize<TomlTable>(toml);
        if (raw is not TomlTable model)
        {
            throw new InvalidOperationException("Structured clarification payload did not deserialize to a TOML table.");
        }
        var id = RequireScalar(model, "id");
        var status = RequireScalar(model, "status");
        if (!string.Equals(status, StructuredClarificationStatus.Open, StringComparison.Ordinal)
            && !string.Equals(status, StructuredClarificationStatus.Answered, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Structured clarification '{id}' has unsupported status '{status}'. Expected '{StructuredClarificationStatus.Open}' or '{StructuredClarificationStatus.Answered}'.");
        }

        var question = RequireScalar(model, "question");
        var background = OptionalScalar(model, "background") ?? string.Empty;
        var recommendation = OptionalScalar(model, "recommendation");
        var blocks = ReadStringArray(model, "blocks");
        var options = ReadOptions(model);
        var answer = ReadAnswer(model);

        if (string.Equals(status, StructuredClarificationStatus.Answered, StringComparison.Ordinal)
            && answer is null)
        {
            throw new InvalidOperationException(
                $"Structured clarification '{id}' is marked answered but has no [answer] table.");
        }

        return new StructuredClarification
        {
            Id = id,
            Status = status,
            Background = background,
            Question = question,
            Options = options,
            Recommendation = recommendation,
            Blocks = blocks,
            Answer = answer,
            SourcePath = sourcePath
        };
    }

    public static string Serialize(StructuredClarification clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);

        var lines = new List<string>
        {
            $"id = \"{Escape(clarification.Id)}\"",
            $"status = \"{Escape(clarification.Status)}\"",
            $"question = \"{Escape(clarification.Question)}\""
        };

        if (!string.IsNullOrEmpty(clarification.Background))
        {
            lines.Add($"background = \"\"\"{Environment.NewLine}{clarification.Background}{Environment.NewLine}\"\"\"");
        }

        if (!string.IsNullOrEmpty(clarification.Recommendation))
        {
            lines.Add($"recommendation = \"{Escape(clarification.Recommendation!)}\"");
        }

        lines.Add($"blocks = [{string.Join(", ", clarification.Blocks.Select(b => $"\"{Escape(b)}\""))}]");

        foreach (var option in clarification.Options)
        {
            lines.Add(string.Empty);
            lines.Add("[[options]]");
            lines.Add($"id = \"{Escape(option.Id)}\"");
            if (!string.IsNullOrEmpty(option.Label))
            {
                lines.Add($"label = \"{Escape(option.Label)}\"");
            }
            lines.Add($"pros = [{string.Join(", ", option.Pros.Select(p => $"\"{Escape(p)}\""))}]");
            lines.Add($"cons = [{string.Join(", ", option.Cons.Select(c => $"\"{Escape(c)}\""))}]");
        }

        if (clarification.Answer is { } answer)
        {
            lines.Add(string.Empty);
            lines.Add("[answer]");
            lines.Add($"choice = \"{Escape(answer.Choice)}\"");
            if (!string.IsNullOrEmpty(answer.Note))
            {
                lines.Add($"note = \"{Escape(answer.Note!)}\"");
            }
            lines.Add($"answered_at = \"{Escape(answer.AnsweredAt)}\"");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string RequireScalar(TomlTable model, string key)
    {
        if (!model.TryGetValue(key, out var raw) || raw is not string value || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Structured clarification is missing required scalar '{key}'.");
        }
        return value;
    }

    private static string? OptionalScalar(TomlTable model, string key)
    {
        if (!model.TryGetValue(key, out var raw) || raw is not string value)
        {
            return null;
        }
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static IReadOnlyList<string> ReadStringArray(TomlTable model, string key)
    {
        if (!model.TryGetValue(key, out var raw))
        {
            return Array.Empty<string>();
        }

        if (raw is not TomlArray array)
        {
            throw new InvalidOperationException($"Structured clarification key '{key}' must be an array of strings.");
        }

        var result = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not string scalar)
            {
                throw new InvalidOperationException($"Structured clarification key '{key}' must contain string entries only.");
            }
            result.Add(scalar);
        }
        return result;
    }

    private static IReadOnlyList<StructuredClarificationOption> ReadOptions(TomlTable model)
    {
        if (!model.TryGetValue("options", out var raw))
        {
            return Array.Empty<StructuredClarificationOption>();
        }

        if (raw is not TomlTableArray tableArray)
        {
            throw new InvalidOperationException("Structured clarification key 'options' must be a [[options]] array of tables.");
        }

        var result = new List<StructuredClarificationOption>(tableArray.Count);
        foreach (var entry in tableArray)
        {
            var id = RequireScalar(entry, "id");
            var label = OptionalScalar(entry, "label") ?? string.Empty;
            var pros = ReadStringArray(entry, "pros");
            var cons = ReadStringArray(entry, "cons");
            result.Add(new StructuredClarificationOption
            {
                Id = id,
                Label = label,
                Pros = pros,
                Cons = cons
            });
        }
        return result;
    }

    private static StructuredClarificationAnswer? ReadAnswer(TomlTable model)
    {
        if (!model.TryGetValue("answer", out var raw))
        {
            return null;
        }

        if (raw is not TomlTable table)
        {
            throw new InvalidOperationException("Structured clarification key 'answer' must be a [answer] table.");
        }

        var choice = RequireScalar(table, "choice");
        var answeredAt = RequireScalar(table, "answered_at");
        var note = OptionalScalar(table, "note");

        return new StructuredClarificationAnswer
        {
            Choice = choice,
            AnsweredAt = answeredAt,
            Note = note
        };
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
