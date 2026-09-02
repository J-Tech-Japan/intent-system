using System.Security.Cryptography;
using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Extracts the command payload from the measured Codex shell approval
/// vocabulary. Recognition is deliberately header-based and conservative;
/// policy evaluation, not this class, decides whether an answer is allowed.
/// </summary>
internal static class ShellCommandPromptRecognizer
{
    private static readonly string[] Headers =
    [
        "Would you like to run the following command?",
        "Would you like to run this command?",
        "Do you want to run the following command?",
        "Do you want to run this command?",
        "Allow this command to run?",
        "Run this command?",
    ];

    public static bool TryExtract(string observedText, out ShellCommandPromptPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(observedText))
        {
            return false;
        }

        var lines = observedText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var headerIndex = Array.FindIndex(lines, line => Headers.Contains(
            Normalize(line), StringComparer.Ordinal));
        if (headerIndex < 0)
        {
            return false;
        }

        var commandLines = new List<string>();
        var insideFence = false;
        var choiceBlockStarted = false;
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            var line = NormalizeContent(rawLine);
            if (line.Length == 0 || IsFrame(line))
            {
                continue;
            }

            if (!insideFence && choiceBlockStarted)
            {
                // Choice text, its wrapped lines, and the terminal hint are
                // dialog chrome. They are never part of the command. A new
                // command marker or fenced block is instead a hidden tail:
                // fail closed rather than authorizing a truncated payload.
                if (line.StartsWith('$')
                    || IsCommandLine(line, out _)
                    || line.StartsWith("```", StringComparison.Ordinal))
                {
                    return false;
                }
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                insideFence = !insideFence;
                continue;
            }

            if (insideFence)
            {
                continue;
            }

            // Choice recognition intentionally consumes the raw line. The
            // content normalizer removes numeric prefixes for headers and
            // payloads, which would otherwise make "1. Yes" indistinguishable
            // from arbitrary prose.
            if (IsChoice(rawLine))
            {
                choiceBlockStarted = true;
                continue;
            }

            if (line.StartsWith("Environment:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsCommandLine(line, out var commandLine))
            {
                commandLines.Add(commandLine);
                continue;
            }

            // A non-choice line is a continuation only after a structural
            // command marker. This keeps header prose and environment chrome
            // out of the payload while retaining wrapped commands.
            if (commandLines.Count > 0)
            {
                commandLines.Add(line);
            }
        }

        // Dialog wrapping is visual chrome. Every fragment collected here is
        // before the first numbered choice, so join wrapped command text as a
        // single shell command rather than turning it into a new segment.
        var command = string.Join(' ', commandLines).Trim();
        if (command.Length == 0)
        {
            return false;
        }

        var parsed = ShellCommandAstParser.Parse(command);
        payload = new ShellCommandPromptPayload
        {
            Command = command,
            Parse = parsed,
            CommandDigest = parsed.Digest,
            DialogHash = Digest(observedText),
        };
        return true;
    }

    private static string Normalize(string line)
    {
        var normalized = NormalizeContent(line);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        if (normalized[0] == '>')
        {
            normalized = normalized[1..].TrimStart();
        }

        var digits = 0;
        while (digits < normalized.Length && char.IsAsciiDigit(normalized[digits]))
        {
            digits++;
        }
        if (digits > 0 && digits < normalized.Length && normalized[digits] is '.' or ')')
        {
            normalized = normalized[(digits + 1)..].TrimStart();
        }

        return normalized;
    }

    private static string NormalizeContent(string line)
    {
        var normalized = line.Trim().Trim('│', '┃', '║').Trim();
        if (normalized.Length > 0 && normalized[0] is '›' or '❯' or '○' or '●')
        {
            normalized = normalized[1..].TrimStart();
        }

        return normalized;
    }

    private static bool IsCommandLine(string line, out string commandLine)
    {
        if (line.StartsWith('$'))
        {
            commandLine = line[1..].TrimStart();
            return commandLine.Length > 0;
        }

        if (line.StartsWith("> ", StringComparison.Ordinal))
        {
            commandLine = line[2..].TrimStart();
            return true;
        }

        if (line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
        {
            commandLine = line["Command:".Length..].TrimStart();
            return true;
        }

        commandLine = string.Empty;
        return false;
    }

    private static bool IsFrame(string line) => line.Length > 0
        && line.All(character => character is '┌' or '└' or '├' or '┤' or '─'
            or '╭' or '╰' or '╯' or '╮' or '┬' or '┴' or '┼');

    private static bool IsChoice(string rawLine)
    {
        var normalized = NormalizeContent(rawLine);
        var lower = normalized.ToLowerInvariant();
        if (lower is "yes" or "no" or "y" or "n" or "allow" or "deny"
            or "cancel" or "reject" or "approve" or "run")
        {
            return true;
        }

        if (lower.StartsWith("yes ", StringComparison.Ordinal)
            || lower.StartsWith("no ", StringComparison.Ordinal)
            || lower.StartsWith("allow ", StringComparison.Ordinal)
            || lower.StartsWith("always allow", StringComparison.Ordinal)
            || lower.StartsWith("continue ", StringComparison.Ordinal)
            || lower.StartsWith("cancel ", StringComparison.Ordinal)
            || lower.StartsWith("reject ", StringComparison.Ordinal))
        {
            return true;
        }

        var digits = 0;
        while (digits < normalized.Length && char.IsAsciiDigit(normalized[digits]))
        {
            digits++;
        }
        return digits > 0
            && digits < normalized.Length
            && normalized[digits] is '.' or ')';
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
