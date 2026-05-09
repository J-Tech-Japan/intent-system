using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntentHostCheckAnalyzerTests
{
    [Fact]
    public void Analyze_FullyInitializedHost_WithMatchingBinding_ReturnsOk()
    {
        var input = NewInput() with
        {
            ConfigTomlPresent = true,
            HostBindingPresent = true,
            BoundHostRepo = "J-Tech-Japan/MyIntentHost",
            ObservedRemoteUrl = "https://github.com/J-Tech-Japan/MyIntentHost.git",
            IntentsDomainDirectoryPresent = true,
            AgentsMarkdownPresent = true,
            ClaudeMarkdownPresent = true,
            QueueStateJsonPresent = true,
            RunsJsonlPresent = true,
            TargetRepo = "J-Tech-Japan/intent-system",
            ChildSubmodulePath = "submodules/intent-system"
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("ok", result.Classification);
        Assert.True(result.Ok);
        Assert.Empty(result.RemediationSteps);
        Assert.Contains("canonical for domain `intent-cli`", result.Summary, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/MyIntentHost", result.Summary, StringComparison.Ordinal);
        Assert.Contains("submodules/intent-system", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_NoConfigToml_ReturnsUninitialized_WithIntentInitRemediation()
    {
        var input = NewInput() with { ConfigTomlPresent = false };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("uninitialized", result.Classification);
        Assert.False(result.Ok);
        Assert.Contains(result.RemediationSteps, s => s.Contains("intent-cli intent init", StringComparison.Ordinal));
        Assert.Contains(result.RemediationSteps, s => s.Contains("re-run `intent host-check`", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_BindingMismatchesObservedRemote_ReturnsWrongHost()
    {
        var input = NewInput() with
        {
            ConfigTomlPresent = true,
            HostBindingPresent = true,
            BoundHostRepo = "J-Tech-Japan/CanonicalHost",
            ObservedRemoteUrl = "https://github.com/J-Tech-Japan/MyIntentHost.git"
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("wrong-host", result.Classification);
        Assert.False(result.Ok);
        Assert.Contains("CanonicalHost", result.Summary, StringComparison.Ordinal);
        Assert.Contains("MyIntentHost", result.Summary, StringComparison.Ordinal);
        // Reuses WrongHostGuard remediation steps
        Assert.Contains(result.RemediationSteps, s => s.Contains("`cd` to the canonical host repo", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ConfigPresentButBindingAbsent_ReturnsMissingHostBinding()
    {
        var input = NewInput() with
        {
            ConfigTomlPresent = true,
            HostBindingPresent = false
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("missing-host-binding", result.Classification);
        Assert.False(result.Ok);
        Assert.Contains(result.RemediationSteps, s => s.Contains("--host-repo", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_BindingPresentButNoIntentsDomainDir_ReturnsDomainNotBootstrapped()
    {
        var input = NewInput() with
        {
            ConfigTomlPresent = true,
            HostBindingPresent = true,
            BoundHostRepo = "J-Tech-Japan/MyIntentHost",
            ObservedRemoteUrl = "https://github.com/J-Tech-Japan/MyIntentHost.git",
            IntentsDomainDirectoryPresent = false
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("domain-not-bootstrapped", result.Classification);
        Assert.False(result.Ok);
        Assert.Contains(result.RemediationSteps, s => s.Contains("intents/intent-cli/", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_DomainBootstrappedButAgentsMissing_ReturnsPartiallyInitialized_WithExactRemediation()
    {
        var input = NewInput() with
        {
            ConfigTomlPresent = true,
            HostBindingPresent = true,
            BoundHostRepo = "J-Tech-Japan/MyIntentHost",
            ObservedRemoteUrl = "https://github.com/J-Tech-Japan/MyIntentHost.git",
            IntentsDomainDirectoryPresent = true,
            AgentsMarkdownPresent = false,
            ClaudeMarkdownPresent = false,
            QueueStateJsonPresent = true,
            RunsJsonlPresent = true,
            TargetRepo = "J-Tech-Japan/intent-system"
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("partially-initialized", result.Classification);
        Assert.False(result.Ok);
        Assert.Contains("AGENTS.md", result.Summary, StringComparison.Ordinal);
        Assert.Contains("CLAUDE.md", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.RemediationSteps, s => s.Contains("intent init", StringComparison.Ordinal) && s.Contains("--write", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_QueueStateMissing_ReturnsPartiallyInitialized_RecommendsHostLoopWake()
    {
        var input = NewInput() with
        {
            ConfigTomlPresent = true,
            HostBindingPresent = true,
            BoundHostRepo = "J-Tech-Japan/MyIntentHost",
            ObservedRemoteUrl = "https://github.com/J-Tech-Japan/MyIntentHost.git",
            IntentsDomainDirectoryPresent = true,
            AgentsMarkdownPresent = true,
            ClaudeMarkdownPresent = true,
            QueueStateJsonPresent = false,
            RunsJsonlPresent = false
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("partially-initialized", result.Classification);
        Assert.Contains(result.RemediationSteps, s => s.Contains("queue-state.json", StringComparison.Ordinal));
        Assert.Contains(result.RemediationSteps, s => s.Contains("runs.jsonl", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_WrongHost_BeatsMissingDomain()
    {
        // Wrong-host should fire BEFORE checking domain-not-bootstrapped, so
        // the operator sees the host-mismatch first.
        var input = NewInput() with
        {
            ConfigTomlPresent = true,
            HostBindingPresent = true,
            BoundHostRepo = "J-Tech-Japan/CanonicalHost",
            ObservedRemoteUrl = "https://github.com/J-Tech-Japan/WrongHost.git",
            IntentsDomainDirectoryPresent = false
        };

        var result = IntentHostCheckAnalyzer.Analyze(input);

        Assert.Equal("wrong-host", result.Classification);
    }

    [Fact]
    public void Analyze_RequiresDomain()
    {
        var input = NewInput() with { Domain = "" };

        Assert.Throws<ArgumentException>(() => IntentHostCheckAnalyzer.Analyze(input));
    }

    private static IntentHostCheckInput NewInput() =>
        new()
        {
            RepoRoot = "/tmp/host",
            Domain = "intent-cli",
            TargetRepo = null,
            ObservedRemoteUrl = null,
            ChildSubmodulePath = null,
            ConfigTomlPresent = false,
            HostBindingPresent = false,
            BoundHostRepo = null,
            IntentsDomainDirectoryPresent = false,
            AgentsMarkdownPresent = false,
            ClaudeMarkdownPresent = false,
            QueueStateJsonPresent = false,
            RunsJsonlPresent = false
        };
}
