using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal static class CrossRuntimeReviewJsonlVerdict
{
    internal static bool TryReadCopilotObservedModel(string content, out string? observedModel, out string error)
    {
        observedModel = null;
        if (!TryReadCopilotEnvelope(content, out _, out observedModel, out _, out error))
        {
            return false;
        }

        error = string.Empty;
        return observedModel is not null;
    }

    internal static bool TryParseCopilot<TVerdict>(
        string content,
        CrossRuntimeReviewVerdict.TryValidateDelegate<TVerdict> validate,
        out TVerdict verdict,
        out string? observedModel,
        out string error)
    {
        verdict = default!;
        observedModel = null;
        if (!TryReadCopilotEnvelope(content, out _, out observedModel, out var answerText, out error))
        {
            return false;
        }

        if (!CrossRuntimeReviewVerdict.TryParseVerdictText(answerText, out var inner, out error))
        {
            error = PrefixCopilotVerdictError(error);
            return false;
        }

        using (inner)
        {
            return validate(inner.RootElement, out verdict, out error);
        }
    }

    private static bool TryReadCopilotEnvelope(
        string content,
        out JsonElement? finalAnswer,
        out string? observedModel,
        out string answerText,
        out string error)
    {
        finalAnswer = null;
        observedModel = null;
        answerText = string.Empty;
        if (!TryReadJsonLines(content, out var lines, out error))
        {
            return false;
        }

        if (lines.Count == 1
            && lines[0].RootElement.TryGetProperty(CrossRuntimeReviewVerdict.FieldVerdict, out _)
            && !lines[0].RootElement.TryGetProperty("type", out _))
        {
            error = "copilot requires the runtime JSON envelope from the pinned invocation; a bare verdict object is refused.";
            return false;
        }

        JsonElement? resultEvent = null;
        var resultCount = 0;
        var finalAnswerCount = 0;

        for (var lineNumber = 1; lineNumber <= lines.Count; lineNumber++)
        {
            var root = lines[lineNumber - 1].RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = $"verdict-invalid: line {lineNumber} must be a JSON object with a string 'type'.";
                return false;
            }

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                error = $"verdict-invalid: line {lineNumber} must be a JSON object with a string 'type'.";
                return false;
            }

            var type = typeElement.GetString()!;
            if (string.Equals(type, "result", StringComparison.Ordinal))
            {
                resultCount++;
                resultEvent = root;
            }
            else if (string.Equals(type, "assistant.message", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("data", out var data))
                {
                    continue;
                }

                if (data.ValueKind != JsonValueKind.Object)
                {
                    error = $"verdict-invalid: line {lineNumber} assistant.message event must have an object data.";
                    return false;
                }

                if (data.TryGetProperty("phase", out var phase)
                    && phase.ValueKind == JsonValueKind.String
                    && string.Equals(phase.GetString(), "final_answer", StringComparison.Ordinal))
                {
                    finalAnswerCount++;
                    finalAnswer = root;
                }
            }
        }

        if (resultCount != 1 || resultEvent is null)
        {
            error = "verdict-invalid: exactly one 'result' event must be present and be the last event.";
            return false;
        }

        if (!string.Equals(lines[^1].RootElement.GetProperty("type").GetString(), "result", StringComparison.Ordinal))
        {
            error = "verdict-invalid: the last event must be the only 'result' event.";
            return false;
        }

        if (!resultEvent.Value.TryGetProperty("exitCode", out var exitCode)
            || exitCode.ValueKind != JsonValueKind.Number
            || !exitCode.TryGetInt32(out var exitCodeValue)
            || exitCodeValue != 0)
        {
            error = "verdict-invalid: the 'result' event must have exitCode 0.";
            return false;
        }

        if (finalAnswerCount != 1 || finalAnswer is null)
        {
            error = "verdict-invalid: exactly one 'assistant.message' event must have data.phase == \"final_answer\".";
            return false;
        }

        if (!finalAnswer.Value.TryGetProperty("data", out var answerData) || answerData.ValueKind != JsonValueKind.Object)
        {
            error = "verdict-invalid: the final_answer event must have an object data with a string data.content.";
            return false;
        }

        if (!answerData.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.String)
        {
            error = "verdict-invalid: the final_answer event must have a string data.content.";
            return false;
        }

        if (answerData.TryGetProperty("toolRequests", out var toolRequests))
        {
            if (toolRequests.ValueKind != JsonValueKind.Array)
            {
                error = "verdict-invalid: the final_answer event data.toolRequests must be absent or an empty array.";
                return false;
            }

            if (toolRequests.GetArrayLength() > 0)
            {
                error = "verdict-invalid: the final_answer event must not have non-empty data.toolRequests.";
                return false;
            }
        }

        if (!answerData.TryGetProperty("model", out var modelElement) || modelElement.ValueKind != JsonValueKind.String)
        {
            observedModel = null;
        }
        else
        {
            observedModel = modelElement.GetString();
        }

        answerText = contentElement.GetString()!;
        return true;
    }

    internal static bool TryParseCopilotEffort(string content, string observedModel, string expectedEffort, out string error)
    {
        error = string.Empty;
        if (!TryReadJsonLines(content, out var lines, out error))
        {
            return false;
        }

        var mainCheckpointCount = 0;
        foreach (var document in lines)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "session.usage_checkpoint")
            {
                continue;
            }

            if (!root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("promptCacheBreakState", out var states)
                || states.ValueKind != JsonValueKind.Array)
            {
                error = $"observed reasoning_effort does not match --effort '{expectedEffort}'.";
                return false;
            }

            foreach (var state in states.EnumerateArray())
            {
                if (state.ValueKind != JsonValueKind.Object
                    || !state.TryGetProperty("conversation", out var conversation)
                    || conversation.ValueKind != JsonValueKind.String)
                {
                    error = $"observed reasoning_effort does not match --effort '{expectedEffort}'.";
                    return false;
                }

                if (!string.Equals(conversation.GetString(), "main", StringComparison.Ordinal))
                {
                    continue;
                }

                mainCheckpointCount++;
                if (!state.TryGetProperty("models", out var models)
                    || models.ValueKind != JsonValueKind.Object
                    || !models.TryGetProperty(observedModel, out var modelState)
                    || modelState.ValueKind != JsonValueKind.Object
                    || !modelState.TryGetProperty("reasoning_effort", out var effort)
                    || effort.ValueKind != JsonValueKind.String
                    || !string.Equals(effort.GetString(), expectedEffort, StringComparison.Ordinal))
                {
                    error = $"observed reasoning_effort does not match --effort '{expectedEffort}'.";
                    return false;
                }
            }
        }

        if (mainCheckpointCount == 0)
        {
            error = $"observed reasoning_effort does not match --effort '{expectedEffort}'.";
            return false;
        }

        return true;
    }

    internal static bool TryParseOpencode<TVerdict>(
        string content,
        CrossRuntimeReviewVerdict.TryValidateDelegate<TVerdict> validate,
        out TVerdict verdict,
        out string answerText,
        out string error)
    {
        verdict = default!;
        answerText = string.Empty;
        if (!TryReadJsonLines(content, out var lines, out error))
        {
            return false;
        }

        if (lines.Count == 1
            && lines[0].RootElement.TryGetProperty(CrossRuntimeReviewVerdict.FieldVerdict, out _)
            && !lines[0].RootElement.TryGetProperty("type", out _))
        {
            error = "opencode requires the runtime JSON envelope from the pinned invocation; a bare verdict object is refused.";
            return false;
        }

        var state = "idle";
        var buffer = string.Empty;

        for (var lineNumber = 1; lineNumber <= lines.Count; lineNumber++)
        {
            var root = lines[lineNumber - 1].RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = $"verdict-invalid: line {lineNumber} must be a JSON object with a string 'type'.";
                return false;
            }

            if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                error = $"verdict-invalid: line {lineNumber} must be a JSON object with a string 'type'.";
                return false;
            }

            var type = typeElement.GetString()!;
            if (!TryAdvanceOpencodeState(ref state, ref buffer, type, root, lineNumber, out error))
            {
                return false;
            }
        }

        if (state != "terminated" || string.IsNullOrEmpty(buffer))
        {
            error = state != "terminated"
                ? "verdict-invalid: T13 refused because the file did not end in state terminated."
                : "verdict-invalid: T13 refused because the final step text is empty.";
            return false;
        }

        answerText = buffer;
        if (!CrossRuntimeReviewVerdict.TryParseVerdictText(answerText, out var inner, out error))
        {
            error = PrefixOpencodeVerdictError(error);
            return false;
        }

        using (inner)
        {
            return validate(inner.RootElement, out verdict, out error);
        }
    }

    internal static bool TryAdvanceOpencodeState(
        ref string state,
        ref string buffer,
        string type,
        JsonElement root,
        int lineNumber,
        out string error)
    {
        error = string.Empty;
        if (string.Equals(type, "error", StringComparison.Ordinal))
        {
            if (!TryFormatOpencodeError(root, lineNumber, out error))
            {
                return false;
            }

            return false;
        }

        if (state == "terminated")
        {
            if (IsCompactionReopenEvent(type, root))
            {
                state = "idle";
                return true;
            }

            error = $"verdict-invalid: T3 refused event '{type}' on line {lineNumber} in state terminated.";
            return false;
        }

        if (string.Equals(type, "text", StringComparison.Ordinal)
            && (state == "idle" || state == "open")
            && IsSyntheticText(root))
        {
            error = $"verdict-invalid: T4 refused event '{type}' on line {lineNumber} in state {state}.";
            return false;
        }

        if (state == "idle" && type == "step_start")
        {
            state = "open";
            buffer = string.Empty;
            return true;
        }

        if (state == "idle" && type is "step_finish" or "text" or "tool_use")
        {
            error = $"verdict-invalid: T6 refused event '{type}' on line {lineNumber} in state idle.";
            return false;
        }

        if (state == "open" && type == "step_start")
        {
            error = $"verdict-invalid: T7 refused event '{type}' on line {lineNumber} in state open.";
            return false;
        }

        if (state == "open" && type == "step_finish")
        {
            if (!root.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Object
                || !part.TryGetProperty("reason", out var reasonElement) || reasonElement.ValueKind != JsonValueKind.String)
            {
                error = $"verdict-invalid: line {lineNumber} step_finish event must have an object 'part' with string 'reason'.";
                return false;
            }

            state = string.Equals(reasonElement.GetString(), "stop", StringComparison.Ordinal) ? "terminated" : "idle";
            return true;
        }

        if (state == "open" && type == "text")
        {
            if (!root.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Object
                || !part.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
            {
                error = $"verdict-invalid: line {lineNumber} text event must have an object 'part' with string 'text'.";
                return false;
            }

            buffer += textElement.GetString();
            return true;
        }

        if (state == "open" && type == "tool_use")
        {
            return true;
        }

        return true;
    }

    internal static bool TryValidateOpencodeExitStatus(string verdictFile, out string cause, out string detail)
    {
        cause = string.Empty;
        detail = string.Empty;
        var directory = Path.GetDirectoryName(verdictFile);
        if (string.IsNullOrEmpty(directory))
        {
            cause = CrossRuntimeReviewCauses.ExitStatusMissing;
            detail = "opencode-exit.txt is missing next to the verdict file.";
            return false;
        }

        var exitPath = Path.Combine(directory, CrossRuntimeReviewFiles.OpencodeExit);
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(
                exitPath,
                maxBytes: 64,
                out var bytes,
                out var readFailure,
                out var readError))
        {
            if (readFailure is CrossRuntimeReviewFileReadFailure.Empty
                or CrossRuntimeReviewFileReadFailure.TooLarge)
            {
                cause = CrossRuntimeReviewCauses.ExitStatusNonzero;
                detail = readFailure == CrossRuntimeReviewFileReadFailure.Empty
                    ? "opencode-exit.txt bytes are (empty); exactly 0\\n is required."
                    : "opencode-exit.txt is too large; exactly 0\\n is required.";
                return false;
            }

            cause = CrossRuntimeReviewCauses.ExitStatusMissing;
            detail = readFailure == CrossRuntimeReviewFileReadFailure.Symlink
                ? "opencode-exit.txt must be a regular file, not a symlink."
                : "opencode-exit.txt must be a regular file next to the verdict file.";
            return false;
        }

        if (bytes.Length != 2 || bytes[0] != (byte)'0' || bytes[1] != (byte)'\n')
        {
            cause = CrossRuntimeReviewCauses.ExitStatusNonzero;
            detail = $"opencode-exit.txt bytes are {FormatExitBytes(bytes)}; exactly 0\\n is required.";
            return false;
        }

        return true;
    }

    private static string FormatExitBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "(empty)";
        }

        var limit = Math.Min(bytes.Length, 16);
        var formatted = string.Concat(bytes.Take(limit).Select(b => $"\\x{b:x2}"));
        return bytes.Length > 16 ? formatted + "..." : formatted;
    }

    private static bool IsCompactionReopenEvent(string type, JsonElement root)
    {
        if (!string.Equals(type, "text", StringComparison.Ordinal))
        {
            return false;
        }

        if (!root.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!part.TryGetProperty("type", out var partType)
            || partType.ValueKind != JsonValueKind.String
            || !string.Equals(partType.GetString(), "text", StringComparison.Ordinal))
        {
            return false;
        }

        if (!part.TryGetProperty("synthetic", out var synthetic) || synthetic.ValueKind != JsonValueKind.True)
        {
            return false;
        }

        if (!part.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return metadata.TryGetProperty("compaction_continue", out var compactionContinue)
            && compactionContinue.ValueKind == JsonValueKind.True;
    }

    private static bool IsSyntheticText(JsonElement root)
    {
        if (!root.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return part.TryGetProperty("synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True;
    }

    private static string PrefixCopilotVerdictError(string error) =>
        error.StartsWith("verdict-invalid:", StringComparison.Ordinal)
            ? error.Replace("verdict text", "copilot final_answer content", StringComparison.Ordinal)
            : error;

    private static string PrefixOpencodeVerdictError(string error) =>
        error.StartsWith("verdict-invalid:", StringComparison.Ordinal)
            ? error.Replace("verdict text", "opencode final step text", StringComparison.Ordinal)
            : error;

    private static bool TryFormatOpencodeError(JsonElement root, int lineNumber, out string error)
    {
        if (!root.TryGetProperty("error", out var errorElement) || errorElement.ValueKind != JsonValueKind.Object)
        {
            error = $"verdict-invalid: line {lineNumber} error event must have an object 'error' with string 'name' and object 'data' with string 'message'.";
            return false;
        }

        if (!errorElement.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            error = $"verdict-invalid: line {lineNumber} error event must have a string error.name.";
            return false;
        }

        if (!errorElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object
            || !dataElement.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
        {
            error = $"verdict-invalid: line {lineNumber} error event must have object error.data with string message.";
            return false;
        }

        error = $"verdict-invalid: T1 refused error event '{nameElement.GetString()}' on line {lineNumber}: {messageElement.GetString()}";
        return true;
    }

    private static bool TryReadJsonLines(string content, out List<JsonDocument> lines, out string error)
    {
        lines = [];
        error = string.Empty;
        if (string.IsNullOrEmpty(content))
        {
            error = "verdict-invalid: file is empty.";
            return false;
        }

        var segments = content.Split('\n');
        if (segments.Length > 0 && segments[^1].Length == 0)
        {
            segments = segments[..^1];
        }

        if (segments.Length == 0)
        {
            error = "verdict-invalid: file is empty.";
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0)
            {
                error = $"verdict-invalid: line {index + 1} is not valid JSON.";
                return false;
            }

            try
            {
                var document = JsonDocument.Parse(segment);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    document.Dispose();
                    error = $"verdict-invalid: line {index + 1} must be a JSON object with a string 'type'.";
                    return false;
                }

                lines.Add(document);
            }
            catch (JsonException exception)
            {
                error = $"verdict-invalid: line {index + 1} is not valid JSON: {exception.Message}";
                return false;
            }
        }

        return true;
    }
}
