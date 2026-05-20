using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G375: Short outer-prompt catalog for common automation requests.
/// The catalog is intentionally lightweight: it tells an operator what
/// to ask a local AI agent, then tells that agent to fetch detailed
/// execution conditions from installed <c>intent-cli</c> guide and
/// automation surfaces. It is read-only, host-state-free, and never
/// launches an AI provider.
/// </summary>
internal static class GuidePromptTemplateCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide prompt-template [--kind <kind>] [--domain <domain>] [--purpose <purpose>] [--agent <agent>] [--frequency <frequency>] [--cwd <cwd>] [--target-repo <owner/repo>] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static readonly IReadOnlyList<PromptTemplateSpec> Templates = new[]
    {
        Template(
            "implementation-loop",
            "Recurring child implementation loop",
            ["<domain>", "<agent>", "<frequency>", "<cwd>", "<owner/repo>"],
            "intent-cli guide workflow task implementation-loop --target-repo <owner/repo> --agent <agent> --frequency <frequency> --format markdown",
            "Please ask intent-cli for the detailed implementation-loop conditions, then set up a current-thread child implementation loop for domain `<domain>` using `<agent>` every `<frequency>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Do not read local rules or skill files; get exact rules from intent-cli.",
            "intent-cli に詳細条件を確認してから、domain `<domain>` の implementation loop を `<agent>` で `<frequency>` 間隔に設定してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。詳細な固定条件は intent-cli から取得してください。"),
        Template(
            "review-next-slice-loop",
            "Recurring host review and next-slice loop",
            ["<domain>", "<agent>", "<frequency>", "<cwd>", "<owner/repo>"],
            "intent-cli guide workflow task review-next-slice-loop --domain <domain> --target-repo <owner/repo> --agent <agent> --frequency <frequency> --format markdown",
            "Please ask intent-cli for the detailed host review-and-next-slice conditions, then set up a current-thread host loop for domain `<domain>` using `<agent>` every `<frequency>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Do not create a remote scheduler or read local rules.",
            "intent-cli に詳細条件を確認してから、domain `<domain>` の review-and-next-slice loop を `<agent>` で `<frequency>` 間隔に設定してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。"),
        Template(
            "implementation-oneshot",
            "One-shot child implementation request",
            ["<domain>", "<agent>", "<cwd>", "<owner/repo>"],
            "intent-cli guide prompt-matrix --mode child-oneshot --target-repo <owner/repo> --agent <agent> --format markdown",
            "Please ask intent-cli for the detailed child one-shot implementation conditions, then implement exactly one ready item for domain `<domain>` using `<agent>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Do not start a loop.",
            "intent-cli に詳細条件を確認してから、domain `<domain>` の ready item を `<agent>` で1件だけ実装してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。ループは作らないでください。"),
        Template(
            "review-next-slice-oneshot",
            "One-shot host review or next-slice request",
            ["<domain>", "<agent>", "<cwd>", "<owner/repo>"],
            "intent-cli guide prompt-matrix --mode host-oneshot --domain <domain> --target-repo <owner/repo> --agent <agent> --format markdown",
            "Please ask intent-cli for the detailed host one-shot review/next-slice conditions, then process exactly one review or next-slice action for domain `<domain>` using `<agent>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Do not create a recurring loop.",
            "intent-cli に詳細条件を確認してから、domain `<domain>` の review または next-slice を `<agent>` で1件だけ処理してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。定期ループは作らないでください。"),
        Template(
            "init-interview",
            "Project init or initial product-owner interview",
            ["<domain>", "<purpose>", "<agent>", "<cwd>", "<owner/repo>"],
            "intent-cli guide workflow task init-host --format json && intent-cli guide workflow task intent-interview --format json",
            "Please ask intent-cli for the init/interview workflow, then help shape `<purpose>` for domain `<domain>` with `<agent>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Ask one focused product-owner question at a time and record only after approval.",
            "intent-cli に init/interview workflow を確認してから、domain `<domain>` の `<purpose>` を `<agent>` で整理してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。質問は1つずつ、承認後だけ記録してください。"),
        Template(
            "packet-create",
            "Clarification, intent organization, or packet creation",
            ["<domain>", "<purpose>", "<agent>", "<cwd>", "<owner/repo>"],
            "intent-cli guide workflow task packet-draft --format json",
            "Please ask intent-cli for the packet/clarification contract, then create or repair the packet for `<purpose>` in domain `<domain>` using `<agent>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Use intent-cli for required sections and stop on hard clarification.",
            "intent-cli に packet/clarification の契約を確認してから、domain `<domain>` の `<purpose>` の packet を `<agent>` で作成または修正してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。"),
        Template(
            "issue-publish",
            "GitHub issue publish request",
            ["<domain>", "<purpose>", "<agent>", "<cwd>", "<owner/repo>"],
            "intent-cli guide workflow task issue-publish --format json",
            "Please ask intent-cli for the issue-publish contract, then publish at most one GitHub issue for `<purpose>` in domain `<domain>` using `<agent>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Apply intent-target only through intent-cli automation surfaces.",
            "intent-cli に issue-publish の契約を確認してから、domain `<domain>` の `<purpose>` の GitHub issue を `<agent>` で最大1件 publish してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。"),
        Template(
            "worker-signal",
            "Worker signal blocker or follow-up",
            ["<domain>", "<purpose>", "<agent>", "<cwd>", "<owner/repo>"],
            "intent-cli guide worker --format json",
            "Please ask intent-cli for the G374 worker signal conditions, then raise or handle one worker signal for `<purpose>` in domain `<domain>` using `<agent>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Use intent-cli worker signal surfaces; do not hand-edit labels.",
            "intent-cli に G374 worker signal の条件を確認してから、domain `<domain>` の `<purpose>` の worker signal を `<agent>` で1件処理してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。"),
        Template(
            "review-signals",
            "Review-side signal collection",
            ["<domain>", "<agent>", "<cwd>", "<owner/repo>"],
            "intent-cli guide review --format json",
            "Please ask intent-cli for the G374 review signal collection conditions, then collect or close out one review signal for domain `<domain>` using `<agent>`. cwd is `<cwd>`. target repo is `<owner/repo>`. Use intent-cli review signal surfaces only.",
            "intent-cli に G374 review signal collection の条件を確認してから、domain `<domain>` の review signal を `<agent>` で1件 collect または close out してください。cwd は `<cwd>`、target repo は `<owner/repo>` です。")
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var kind, out var format, out var values, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var selected = string.IsNullOrWhiteSpace(kind)
            ? Templates
            : Templates.Where(t => string.Equals(t.Kind, kind, StringComparison.Ordinal)).ToArray();

        if (selected.Count == 0)
        {
            writer.WriteLine($"Unknown --kind '{kind}'. Supported: {string.Join(", ", Templates.Select(t => t.Kind))}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var rendered = selected.Select(t => Render(t, values)).ToArray();
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(kind is null
                ? JsonSerializer.Serialize(rendered, JsonOptions)
                : JsonSerializer.Serialize(rendered[0], JsonOptions));
            writer.WriteLine();
            return 0;
        }

        WriteMarkdown(writer, rendered);
        return 0;
    }

    private static PromptTemplateSpec Template(
        string kind,
        string title,
        IReadOnlyList<string> placeholders,
        string detailedGuideCommand,
        string prompt,
        string japanesePrompt)
    {
        return new PromptTemplateSpec(kind, title, placeholders, detailedGuideCommand, prompt, japanesePrompt);
    }

    private static PromptTemplate Render(PromptTemplateSpec spec, IReadOnlyDictionary<string, string> values)
    {
        return new PromptTemplate(
            spec.Kind,
            spec.Title,
            spec.Placeholders,
            spec.DetailedGuideCommand,
            ApplyValues(spec.Prompt, values),
            ApplyValues(spec.JapanesePrompt, values),
            [
                "Keep the outer prompt short; detailed fixed conditions must be obtained from intent-cli during execution.",
                "Do not read local rules, local skill files, or copied prompt files for routine workflow conditions.",
                "Do not ask intent-cli to launch Claude, Codex, Copilot, or any AI provider."
            ]);
    }

    private static string ApplyValues(string template, IReadOnlyDictionary<string, string> values)
    {
        var output = template;
        foreach (var (placeholder, value) in values)
        {
            output = output.Replace(placeholder, value, StringComparison.Ordinal);
        }

        return output;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string format,
        out IReadOnlyDictionary<string, string> values,
        out string error)
    {
        kind = null;
        format = FormatMarkdown;
        error = string.Empty;

        var parsedValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<domain>"] = "<domain>",
            ["<purpose>"] = "<purpose>",
            ["<agent>"] = "<agent>",
            ["<frequency>"] = "<frequency>",
            ["<cwd>"] = "<cwd>",
            ["<owner/repo>"] = "<owner/repo>"
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (RequiresValue(arg))
            {
                if (i + 1 >= args.Length)
                {
                    values = parsedValues;
                    error = $"{arg} requires a value.";
                    return false;
                }

                var value = args[++i];
                switch (arg)
                {
                    case "--kind":
                        kind = value;
                        break;
                    case "--format":
                        if (!string.Equals(value, FormatMarkdown, StringComparison.Ordinal)
                            && !string.Equals(value, FormatJson, StringComparison.Ordinal))
                        {
                            values = parsedValues;
                            error = "--format must be 'markdown' or 'json'.";
                            return false;
                        }
                        format = value;
                        break;
                    case "--domain":
                        parsedValues["<domain>"] = value;
                        break;
                    case "--purpose":
                        parsedValues["<purpose>"] = value;
                        break;
                    case "--agent":
                        parsedValues["<agent>"] = value;
                        break;
                    case "--frequency":
                        parsedValues["<frequency>"] = value;
                        break;
                    case "--cwd":
                        parsedValues["<cwd>"] = value;
                        break;
                    case "--target-repo":
                        parsedValues["<owner/repo>"] = value;
                        break;
                }
            }
            else
            {
                values = parsedValues;
                error = $"Unknown argument '{arg}'.";
                return false;
            }
        }

        values = parsedValues;
        return true;
    }

    private static bool RequiresValue(string arg)
    {
        return string.Equals(arg, "--kind", StringComparison.Ordinal)
            || string.Equals(arg, "--format", StringComparison.Ordinal)
            || string.Equals(arg, "--domain", StringComparison.Ordinal)
            || string.Equals(arg, "--purpose", StringComparison.Ordinal)
            || string.Equals(arg, "--agent", StringComparison.Ordinal)
            || string.Equals(arg, "--frequency", StringComparison.Ordinal)
            || string.Equals(arg, "--cwd", StringComparison.Ordinal)
            || string.Equals(arg, "--target-repo", StringComparison.Ordinal);
    }

    private static void WriteMarkdown(TextWriter writer, IReadOnlyList<PromptTemplate> templates)
    {
        writer.WriteLine("# Guide prompt-template catalog");
        writer.WriteLine();
        writer.WriteLine("Short prompts for common automation requests. Detailed fixed conditions must be obtained from `intent-cli` during execution.");

        foreach (var template in templates)
        {
            writer.WriteLine();
            writer.WriteLine($"## {template.Kind}");
            writer.WriteLine();
            writer.WriteLine($"- title: {template.Title}");
            writer.WriteLine($"- detailed guide: `{template.DetailedGuideCommand}`");
            writer.WriteLine($"- placeholders: {string.Join(", ", template.Placeholders.Select(p => $"`{p}`"))}");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(template.Prompt);
            writer.WriteLine("```");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(template.JapanesePrompt);
            writer.WriteLine("```");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide prompt-template");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Kinds:");
        foreach (var template in Templates)
        {
            writer.WriteLine($"- {template.Kind} — {template.Title}");
        }
        writer.WriteLine();
        writer.WriteLine("Detailed fixed conditions must be obtained from intent-cli during execution; this command only renders the short outer request.");
    }
}

internal sealed record PromptTemplateSpec(
    string Kind,
    string Title,
    IReadOnlyList<string> Placeholders,
    string DetailedGuideCommand,
    string Prompt,
    string JapanesePrompt);

internal sealed record PromptTemplate(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("placeholders")] IReadOnlyList<string> Placeholders,
    [property: JsonPropertyName("detailed_guide_command")] string DetailedGuideCommand,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("japanese_prompt")] string JapanesePrompt,
    [property: JsonPropertyName("guardrails")] IReadOnlyList<string> Guardrails);
