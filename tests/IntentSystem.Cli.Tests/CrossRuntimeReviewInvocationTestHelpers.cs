using System.Text;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>G842: shared invocation tokenization and deny-list checks for cross-runtime review tests.</summary>
internal static class CrossRuntimeReviewInvocationTestHelpers
{
    internal static List<string> ShellTokens(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];
            if (character == '\'')
            {
                inToken = true;
                var end = command.IndexOf('\'', index + 1);
                current.Append(command, index + 1, end - index - 1);
                index = end;
            }
            else if (character == '"')
            {
                inToken = true;
                var end = command.IndexOf('"', index + 1);
                current.Append(command, index + 1, end - index - 1);
                index = end;
            }
            else if (character == '\\' && index + 1 < command.Length)
            {
                inToken = true;
                current.Append(command[++index]);
            }
            else if (char.IsWhiteSpace(character))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
            }
            else
            {
                inToken = true;
                current.Append(character);
            }
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    internal static string InvocationBodyForAllowListChecks(string runtime, string command, string outDir)
    {
        if (!string.Equals(runtime, CrossRuntimeReviewRuntimes.Opencode, StringComparison.Ordinal))
        {
            return command;
        }

        var exitQuoted = CrossRuntimeReviewPaths.ShellQuote(Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeExit));
        var rmPrefix = $"rm -f {exitQuoted};";
        var printfSuffix = $"; printf '%s\\n' \"$?\" > {exitQuoted}";
        if (!command.StartsWith(rmPrefix, StringComparison.Ordinal)
            || !command.EndsWith(printfSuffix, StringComparison.Ordinal))
        {
            return command;
        }

        return command[rmPrefix.Length..^printfSuffix.Length];
    }

    internal static void AssertNoDenyFlags(string runtime, string command)
    {
        var tokens = ShellTokens(command);
        foreach (var denied in CrossRuntimeReviewRuntimes.DenyFlags[runtime])
        {
            if (denied.Contains(' ', StringComparison.Ordinal))
            {
                var parts = denied.Split(' ');
                Assert.False(
                    ContainsConsecutiveTokens(tokens, parts),
                    $"deny-list entry '{denied}' appears in: {command}");
            }
            else
            {
                Assert.DoesNotContain(denied, tokens);
            }
        }
    }

    private static bool ContainsConsecutiveTokens(IReadOnlyList<string> tokens, IReadOnlyList<string> parts)
    {
        if (parts.Count == 0 || tokens.Count < parts.Count)
        {
            return false;
        }

        for (var index = 0; index <= tokens.Count - parts.Count; index++)
        {
            var matched = true;
            for (var partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                if (!string.Equals(tokens[index + partIndex], parts[partIndex], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }
}
