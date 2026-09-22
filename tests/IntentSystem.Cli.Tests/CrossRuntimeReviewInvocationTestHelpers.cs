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

    internal static void AssertCopilotEnvironmentAssignments(
        string command,
        string outDir,
        string expectedGhConfigDir)
    {
        var tokens = ShellTokens(command);
        Assert.True(tokens.Count >= 5, $"copilot invocation is missing its environment prefix: {command}");
        Assert.Equal("COPILOT_ALLOW_ALL=", tokens[0]);
        Assert.Equal(
            $"COPILOT_HOME={Path.GetFullPath(Path.Combine(outDir, CrossRuntimeReviewFiles.CopilotHome))}",
            tokens[1]);
        Assert.Equal(
            $"XDG_CONFIG_HOME={Path.GetFullPath(Path.Combine(outDir, CrossRuntimeReviewFiles.CopilotXdg))}",
            tokens[2]);

        var ghAssignment = tokens[3];
        Assert.Equal("copilot", tokens[4]);
        Assert.True(
            tokens.Count(token => token.StartsWith("GH_CONFIG_DIR=", StringComparison.Ordinal)) == 1,
            $"GH_CONFIG_DIR must appear once in the fixed environment prefix: {command}");
        Assert.StartsWith("GH_CONFIG_DIR=", ghAssignment, StringComparison.Ordinal);

        var ghConfigDir = ghAssignment["GH_CONFIG_DIR=".Length..];
        Assert.NotEmpty(ghConfigDir);
        Assert.True(Path.IsPathRooted(ghConfigDir), $"GH_CONFIG_DIR must be absolute: {command}");
        Assert.False(
            IsInsideOrEqual(ghConfigDir, outDir),
            $"GH_CONFIG_DIR must not point inside the out-dir: {command}");
        Assert.Equal(Path.GetFullPath(expectedGhConfigDir), Path.GetFullPath(ghConfigDir));
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
                if (IsEnvironmentVariableName(denied))
                {
                    var assignmentPrefix = denied + "=";
                    Assert.False(
                        tokens.Any(token => token.StartsWith(assignmentPrefix, StringComparison.Ordinal)
                            && token.Length > assignmentPrefix.Length),
                        $"deny-list entry '{denied}' has a non-empty assignment in: {command}");
                }
            }
        }
    }

    private static bool IsEnvironmentVariableName(string value) =>
        value.Length > 0
        && value.All(character => character == '_'
            || (character >= 'A' && character <= 'Z')
            || (character >= '0' && character <= '9'));

    private static bool IsInsideOrEqual(string path, string root)
    {
        var normalizedPath = TrimTrailingSeparators(Path.GetFullPath(path));
        var normalizedRoot = TrimTrailingSeparators(Path.GetFullPath(root));
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar
                && normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSeparators(string value) =>
        value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
