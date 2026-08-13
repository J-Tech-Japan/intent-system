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
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var line = Normalize(lines[index]);
            if (line.Length == 0 || IsFrame(line))
            {
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                insideFence = !insideFence;
                continue;
            }

            if (!insideFence && IsChoice(line))
            {
                if (commandLines.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (line.StartsWith("$ ", StringComparison.Ordinal)
                || line.StartsWith("> ", StringComparison.Ordinal))
            {
                line = line[2..].TrimStart();
            }
            if (line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase))
            {
                line = line["Command:".Length..].TrimStart();
            }
            if (line.Length > 0)
            {
                commandLines.Add(line);
            }
        }

        var command = string.Join('\n', commandLines).Trim();
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
        var normalized = line.Trim().Trim('│', '┃', '║').Trim();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        if (normalized[0] is '›' or '❯' or '○' or '●' or '>')
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

    private static bool IsFrame(string line) => line.Length > 0
        && line.All(character => character is '┌' or '└' or '├' or '┤' or '─'
            or '╭' or '╰' or '╯' or '╮' or '┬' or '┴' or '┼');

    private static bool IsChoice(string line)
    {
        var normalized = Normalize(line);
        var lower = normalized.ToLowerInvariant();
        if (lower is "yes" or "no" or "y" or "n" or "allow" or "deny"
            or "cancel" or "reject" or "approve" or "run")
        {
            return true;
        }

        if (lower.Contains("[y/n]", StringComparison.Ordinal)
            || lower.Contains("[yes/no]", StringComparison.Ordinal)
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
