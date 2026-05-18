using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G365: covers <see cref="HostBindingDomainResolver"/>'s on-disk
/// resolution behavior (parent-root vs child-root precedence, parse
/// tolerance, case-insensitive target_repo matching) so the
/// host-loop-next-action and host-review-diagnostics commands can
/// trust the lookup. Each test owns a temp workspace and deletes it
/// on dispose.
/// </summary>
public sealed class HostBindingDomainResolverTests : IDisposable
{
    private readonly string _root;

    public HostBindingDomainResolverTests()
    {
        _root = Directory.CreateTempSubdirectory("host-binding-domain-resolver-tests-").FullName;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ReturnsMissing_WhenBindingFileAbsent()
    {
        var context = CreateContext(_root, parentRoot: null);

        var result = HostBindingDomainResolver.Resolve(context, "owner/repo");

        Assert.Equal(HostBindingDomainResolutionKind.Missing, result.Kind);
        Assert.Null(result.Domain);
        Assert.Null(result.BoundTargetRepo);
    }

    [Fact]
    public void Resolve_ReturnsMatch_WhenTargetRepoMatchesCaseInsensitive()
    {
        WriteBinding(_root, """
        [host]
        domain = "sekiban-as-a-service"
        host_repo = "J-Tech-Japan/intent-system"
        target_repo = "J-Tech-Japan/SekibanAsAService"
        """);

        var context = CreateContext(_root, parentRoot: null);
        var result = HostBindingDomainResolver.Resolve(context, "j-tech-japan/sekibanasaservice");

        Assert.Equal(HostBindingDomainResolutionKind.Match, result.Kind);
        Assert.Equal("sekiban-as-a-service", result.Domain);
    }

    [Fact]
    public void Resolve_ReturnsMismatch_WhenTargetRepoDoesNotMatch()
    {
        WriteBinding(_root, """
        [host]
        domain = "intent-cli"
        host_repo = "J-Tech-Japan/intent-system"
        target_repo = "J-Tech-Japan/intent-system"
        """);

        var context = CreateContext(_root, parentRoot: null);
        var result = HostBindingDomainResolver.Resolve(context, "J-Tech-Japan/OtherRepo");

        Assert.Equal(HostBindingDomainResolutionKind.Mismatch, result.Kind);
        Assert.Equal("intent-cli", result.Domain);
        Assert.Equal("J-Tech-Japan/intent-system", result.BoundTargetRepo);
    }

    [Fact]
    public void Resolve_ReturnsMissing_WhenBindingHasEmptyTargetRepo()
    {
        WriteBinding(_root, """
        [host]
        domain = "intent-cli"
        host_repo = "J-Tech-Japan/intent-system"
        target_repo = ""
        """);

        var context = CreateContext(_root, parentRoot: null);
        var result = HostBindingDomainResolver.Resolve(context, "owner/repo");

        Assert.Equal(HostBindingDomainResolutionKind.Missing, result.Kind);
    }

    [Fact]
    public void Resolve_PrefersParentBindingOverChildBinding()
    {
        // G365: when a parent intent repo root is configured, the
        // parent's host-binding wins so a stale child workspace
        // binding cannot override the host's authoritative mapping.
        var parentRoot = Directory.CreateDirectory(Path.Combine(_root, "parent-host")).FullName;
        var childRoot = Directory.CreateDirectory(Path.Combine(_root, "child-workspace")).FullName;
        WriteBinding(parentRoot, """
        [host]
        domain = "parent-domain"
        host_repo = "owner/parent"
        target_repo = "owner/target"
        """);
        WriteBinding(childRoot, """
        [host]
        domain = "child-domain"
        host_repo = "owner/child"
        target_repo = "owner/target"
        """);

        var context = CreateContext(childRoot, parentRoot: parentRoot);
        var result = HostBindingDomainResolver.Resolve(context, "owner/target");

        Assert.Equal(HostBindingDomainResolutionKind.Match, result.Kind);
        Assert.Equal("parent-domain", result.Domain);
    }

    [Fact]
    public void ParseHostBinding_TolerantOfCommentsAndQuotes()
    {
        var content = """
        # leading comment
        [host]
          # nested comment is ignored
        domain = 'single-quoted-domain' # trailing inline comment
        host_repo = "owner/repo"
        target_repo = unquoted/value
        """;

        var (domain, targetRepo) = HostBindingDomainResolver.ParseHostBinding(content);

        Assert.Equal("single-quoted-domain", domain);
        Assert.Equal("unquoted/value", targetRepo);
    }

    [Fact]
    public void ParseHostBinding_ReturnsBlankWhenNoHostSection()
    {
        var content = "[other]\ndomain = \"x\"\ntarget_repo = \"y\"\n";

        var (domain, targetRepo) = HostBindingDomainResolver.ParseHostBinding(content);

        Assert.Null(domain);
        Assert.Null(targetRepo);
    }

    private static void WriteBinding(string repoRoot, string content)
    {
        var dir = Path.Combine(repoRoot, ".intent-cli");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "host-binding.toml"), content);
    }

    private static CliContext CreateContext(string repoRoot, string? parentRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = string.Empty,
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                    ParentIntentRepoRoot = parentRoot ?? string.Empty,
                }
            }
        };
    }
}
