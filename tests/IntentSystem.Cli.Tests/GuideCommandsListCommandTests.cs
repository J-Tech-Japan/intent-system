using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideCommandsListCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsTableForAllGroups()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide commands — top-level groups", output, StringComparison.Ordinal);
        // G467: the table now carries the operator-role column.
        Assert.Contains("| group | role | classification | mutability | caller | purpose |", output, StringComparison.Ordinal);
        Assert.Contains("Operator-role categories (G467)", output, StringComparison.Ordinal);
        foreach (var group in new[] { "guide", "intent", "interview", "packet", "worker", "automation", "metadata", "review", "closeout", "issue", "queue", "grill", "stack", "next", "inspect" })
        {
            Assert.Contains(group, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_Json_IncludesRoleMetadataAndDesignSideCommands()
    {
        // G467: every entry carries an operator-role category, and the new
        // design-side commands appear in the catalog.
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var groups = document.RootElement.GetProperty("groups");
        var byName = groups.EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e);

        // New design-side commands present and categorized as design.
        foreach (var name in new[] { "grill", "stack", "next", "inspect", "improve" })
        {
            Assert.True(byName.ContainsKey(name), $"catalog missing '{name}'");
            Assert.Equal("design", byName[name].GetProperty("role").GetString());
        }

        // Role coverage spans all five operator-role categories.
        var roles = groups.EnumerateArray().Select(e => e.GetProperty("role").GetString()).ToHashSet();
        foreach (var role in new[] { "design", "host-review", "child-implementation", "recovery-diagnostics", "advanced-developer" })
        {
            Assert.Contains(role, roles);
        }

        // worker is the child-implementation surface.
        Assert.Equal("child-implementation", byName["worker"].GetProperty("role").GetString());
        Assert.Contains("ruling", byName.Keys);
        Assert.Equal("design", byName["ruling"].GetProperty("role").GetString());
        Assert.Contains("Local immutable operator-ruling", byName["ruling"].GetProperty("purpose").GetString(), StringComparison.Ordinal);

        // Loop-prompt creation surfaces are discoverable in the catalog —
        // prompt-template AND prompt-matrix each as their own role-categorized
        // entry (G467 review: prompt-matrix must be a discoverable entry, not
        // only mentioned inside prompt-template's purpose text).
        Assert.Contains("guide workflow task implementation-loop", byName.Keys);
        Assert.Contains("guide workflow task review-next-slice-loop", byName.Keys);
        Assert.Contains("guide prompt-template", byName.Keys);
        Assert.Contains("guide prompt-matrix", byName.Keys);
        Assert.Equal("design", byName["guide prompt-matrix"].GetProperty("role").GetString());
        Assert.Equal("support", byName["guide prompt-matrix"].GetProperty("classification").GetString());

        var sessionLayerPurpose = byName["session-layer"].GetProperty("purpose").GetString()!;
        Assert.Contains("fewer dependencies", sessionLayerPurpose, StringComparison.Ordinal);
        Assert.Contains("deprecated `agmsg` + herdr", sessionLayerPurpose, StringComparison.Ordinal); // G829: supersedes "supported, non-retired"
        Assert.DoesNotContain("`agmsg` remains PRIMARY", sessionLayerPurpose, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationValidatorIsReachableFromAllThreeGuideRoutesInBothFormats()
    {
        const string command = "intent-cli packet validate-verification --execution-unit <unit> --format json";
        const string implementedLocalScope = "G866/G867 provide local supplied-TRX binding and read-only coverage";
        foreach (var format in new[] { "json", "markdown" })
        {
            using var catalogWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "commands", "list", "--format", format], CreateContext(), catalogWriter));
            var catalogText = catalogWriter.ToString();
            string packetPurpose;
            if (format == "json")
            {
                using var catalog = JsonDocument.Parse(catalogText);
                packetPurpose = catalog.RootElement.GetProperty("groups").EnumerateArray()
                    .Single(group => group.GetProperty("name").GetString() == "packet")
                    .GetProperty("purpose").GetString()!;
            }
            else packetPurpose = catalogText;
            Assert.Contains("packet validate-verification", packetPurpose, StringComparison.Ordinal);
            Assert.Contains("structural coverage", packetPurpose, StringComparison.Ordinal);
            Assert.Contains("never runs commands", packetPurpose, StringComparison.Ordinal);
            Assert.Contains("not a gate", packetPurpose, StringComparison.Ordinal);
            Assert.Contains(implementedLocalScope, packetPurpose, StringComparison.Ordinal);

            using var soloWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "solo-conductor", "--format", format], CreateContext(), soloWriter));
            var soloText = soloWriter.ToString();
            string soloInstruction;
            if (format == "json")
            {
                using var solo = JsonDocument.Parse(soloText);
                var step = solo.RootElement.GetProperty("loop").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "packet-draft-and-validate");
                soloInstruction = step.GetProperty("instruction").GetString()!;
                Assert.Contains(step.GetProperty("commands").EnumerateArray(), item => item.GetProperty("command").GetString() == command);
            }
            else soloInstruction = soloText;
            Assert.Contains("packet validate-verification", soloInstruction, StringComparison.Ordinal);
            Assert.Contains("structural coverage", soloInstruction, StringComparison.Ordinal);
            Assert.Contains("never runs commands", soloInstruction, StringComparison.Ordinal);
            Assert.Contains("does not gate later steps", soloInstruction, StringComparison.Ordinal);
            Assert.Contains(implementedLocalScope, soloInstruction, StringComparison.Ordinal);

            using var publishWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "workflow", "task", "issue-publish", "--format", format], CreateContext(), publishWriter));
            var publishText = publishWriter.ToString();
            string publishGuidance;
            if (format == "json")
            {
                using var publish = JsonDocument.Parse(publishText);
                publishGuidance = string.Join(" ", publish.RootElement.GetProperty("invariants").EnumerateArray().Select(item => item.GetString()));
            }
            else publishGuidance = publishText;
            Assert.Contains(command, publishGuidance, StringComparison.Ordinal);
            Assert.Contains("structural coverage", publishGuidance, StringComparison.Ordinal);
            Assert.Contains("never runs commands", publishGuidance, StringComparison.Ordinal);
            Assert.Contains("not a gate", publishGuidance, StringComparison.Ordinal);
            Assert.Contains(implementedLocalScope, publishGuidance, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VerificationEvidenceSemanticsAreDiscoverableFromAllThreeGuideRoutes()
    {
        const string record = "verification evidence record";
        const string localOnly = "publication not verified";
        const string privacyNote = "Embedded TRX may contain test names, stdout, or machine paths; committing/pushing the artifact publishes those bytes, and this command does not scan them for privacy.";
        foreach (var format in new[] { "json", "markdown" })
        {
            using var catalogWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "commands", "list", "--format", format], CreateContext(), catalogWriter));
            var catalogText = catalogWriter.ToString();
            string purpose;
            if (format == "json")
            {
                using var catalog = JsonDocument.Parse(catalogText);
                purpose = catalog.RootElement.GetProperty("groups").EnumerateArray()
                    .Single(group => group.GetProperty("name").GetString() == "verification")
                    .GetProperty("purpose").GetString()!;
            }
            else purpose = catalogText;
            Assert.Contains(record, purpose, StringComparison.Ordinal);
            Assert.Contains("only explicit `--write` stores locally", purpose, StringComparison.Ordinal);
            Assert.Contains("not-observed", purpose, StringComparison.Ordinal);
            Assert.Contains("does not infer an AC passed", purpose, StringComparison.Ordinal);
            Assert.Contains(localOnly, purpose, StringComparison.Ordinal);
            Assert.Contains("fresh clone", purpose, StringComparison.Ordinal);
            Assert.Contains(privacyNote, purpose, StringComparison.Ordinal);

            using var soloWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "solo-conductor", "--format", format], CreateContext(), soloWriter));
            string soloText;
            if (format == "json")
            {
                using var solo = JsonDocument.Parse(soloWriter.ToString());
                soloText = solo.RootElement.GetProperty("loop").EnumerateArray()
                    .Single(step => step.GetProperty("id").GetString() == "packet-draft-and-validate")
                    .GetProperty("instruction").GetString()!;
            }
            else soloText = soloWriter.ToString();
            Assert.Contains(record, soloText, StringComparison.Ordinal);
            Assert.Contains("--write` stores one canonical local artifact", soloText, StringComparison.Ordinal);
            Assert.Contains("acceptance is `not-assessed`", soloText, StringComparison.Ordinal);
            Assert.Contains("Hosted-required proofs and", soloText, StringComparison.Ordinal);
            Assert.Contains("deferral are unsupported", soloText, StringComparison.Ordinal);
            Assert.Contains("commit and plain-push it", soloText, StringComparison.Ordinal);
            Assert.Contains("fresh clone", soloText, StringComparison.Ordinal);
            Assert.Contains(privacyNote, soloText, StringComparison.Ordinal);

            using var workflowWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "workflow", "task", "issue-publish", "--format", format], CreateContext(), workflowWriter));
            var workflowText = workflowWriter.ToString();
            string invariants;
            if (format == "json")
            {
                using var workflow = JsonDocument.Parse(workflowText);
                invariants = string.Join(" ", workflow.RootElement.GetProperty("invariants").EnumerateArray().Select(item => item.GetString()));
            }
            else invariants = workflowText;
            Assert.Contains(record, invariants, StringComparison.Ordinal);
            Assert.Contains("previews by default", invariants, StringComparison.Ordinal);
            Assert.Contains("execution is `not-observed`", invariants, StringComparison.Ordinal);
            Assert.Contains("does not infer an AC passed", invariants, StringComparison.Ordinal);
            Assert.Contains("Hosted-required proof", invariants, StringComparison.Ordinal);
            Assert.Contains("deferral are unsupported", invariants, StringComparison.Ordinal);
            Assert.Contains("Commit and plain-push a record", invariants, StringComparison.Ordinal);
            Assert.Contains("fresh clone", invariants, StringComparison.Ordinal);
            Assert.Contains(privacyNote, invariants, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VerificationCoverageIsDiscoveredAsLocalAdvisoryFromAllThreeGuideRoutes()
    {
        const string command = "intent-cli verification evidence coverage --execution-unit <unit> --domain <domain> --team <team> --repo <owner/repo> --head-sha <40-lowerhex> --packet-sha256 <64-lowerhex> [--evidence <proof-id>=<record-id>]...";
        foreach (var format in new[] { "json", "markdown" })
        {
            using var catalogWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "commands", "list", "--format", format], CreateContext(), catalogWriter));
            string catalogPurpose;
            if (format == "json")
            {
                using var catalog = JsonDocument.Parse(catalogWriter.ToString());
                catalogPurpose = catalog.RootElement.GetProperty("groups").EnumerateArray()
                    .Single(group => group.GetProperty("name").GetString() == "verification")
                    .GetProperty("purpose").GetString()!;
            }
            else catalogPurpose = catalogWriter.ToString();
            Assert.Contains(command, catalogPurpose, StringComparison.Ordinal);
            Assert.Contains("at most 16", catalogPurpose, StringComparison.Ordinal);
            Assert.Contains("hosted-required", catalogPurpose, StringComparison.Ordinal);
            Assert.Contains("does not change lifecycle status/outcome/exit", catalogPurpose, StringComparison.Ordinal);

            using var soloWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "solo-conductor", "--format", format], CreateContext(), soloWriter));
            string soloInstruction;
            if (format == "json")
            {
                using var solo = JsonDocument.Parse(soloWriter.ToString());
                var step = solo.RootElement.GetProperty("loop").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "packet-draft-and-validate");
                soloInstruction = step.GetProperty("instruction").GetString()!;
                Assert.Contains(step.GetProperty("commands").EnumerateArray(),
                    item => item.GetProperty("command").GetString() == command + " --format json");
            }
            else soloInstruction = soloWriter.ToString();
            Assert.Contains(command, soloInstruction, StringComparison.Ordinal);
            Assert.Contains("not-applicable/not-observed", soloInstruction, StringComparison.Ordinal);
            Assert.Contains("does not change lifecycle status, outcome, or exit", soloInstruction, StringComparison.Ordinal);

            using var workflowWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "workflow", "task", "issue-publish", "--format", format], CreateContext(), workflowWriter));
            string workflowInvariants;
            if (format == "json")
            {
                using var workflow = JsonDocument.Parse(workflowWriter.ToString());
                workflowInvariants = string.Join(" ", workflow.RootElement.GetProperty("invariants").EnumerateArray()
                    .Select(item => item.GetString()));
            }
            else workflowInvariants = workflowWriter.ToString();
            Assert.Contains(command, workflowInvariants, StringComparison.Ordinal);
            Assert.Contains("at most 16", workflowInvariants, StringComparison.Ordinal);
            Assert.Contains("hosted-required", workflowInvariants, StringComparison.Ordinal);
            Assert.Contains("does not change lifecycle status, outcome, exit", workflowInvariants, StringComparison.Ordinal);
            Assert.Contains("publication", workflowInvariants, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void G866DocumentationIsAlignedInEnglishAndJapanese()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var reference = File.ReadAllText(Path.Combine(root, "docs", language, "08-command-reference.md"));
            var orchestration = File.ReadAllText(Path.Combine(root, "docs", language, "12-agent-message-orchestration.md"));
            var ledger = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("G866", reference, StringComparison.Ordinal);
            Assert.Contains("verification evidence record|show|validate", reference, StringComparison.Ordinal);
            Assert.Contains("not-observed", reference, StringComparison.Ordinal);
            Assert.Contains("fresh clone", reference, StringComparison.Ordinal);
            Assert.Contains("G866", orchestration, StringComparison.Ordinal);
            Assert.Contains("G866", ledger, StringComparison.Ordinal);
            if (language == "en")
            {
                AssertPrivacyNoteFragments(reference, "Embedded TRX may contain", "this command does not scan them for privacy.");
                AssertPrivacyNoteFragments(orchestration, "Embedded TRX may contain", "this command does not scan them for privacy.");
                AssertPrivacyNoteFragments(ledger, "Embedded TRX may contain", "this command does not scan them for privacy.");
                Assert.Contains("Hosted-required proofs and", orchestration, StringComparison.Ordinal);
                Assert.Contains("deferral are unsupported", orchestration, StringComparison.Ordinal);
                Assert.Contains("no automatic Git mutation", ledger, StringComparison.Ordinal);
            }
            else
            {
                AssertPrivacyNoteFragments(reference, "埋め込まれた TRX にはテスト名", "この command は個人情報の検査を行いません。");
                AssertPrivacyNoteFragments(orchestration, "埋め込まれた TRX にはテスト名", "この command は個人情報の検査を行いません。");
                AssertPrivacyNoteFragments(ledger, "埋め込まれた TRX にはテスト名", "この command は個人情報の検査を行いません。");
                Assert.Contains("`hosted-required` proof", orchestration, StringComparison.Ordinal);
                Assert.Contains("deferral は未対応", orchestration, StringComparison.Ordinal);
                Assert.Contains("自動 Git mutation はありません", ledger, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void G867DocumentationCoversReadOnlyLocalCoverageAndAdvisorySummary()
    {
        foreach (var language in new[] { "en", "ja" })
        {
            var reference = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "08-command-reference.md"));
            var orchestration = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md"));
            var ledger = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "1.0-compatibility-ledger.md"));
            foreach (var document in new[] { reference, orchestration, ledger })
            {
                Assert.Contains("G867", document, StringComparison.Ordinal);
                Assert.Contains("verification evidence coverage", document, StringComparison.Ordinal);
                Assert.Contains("--verification-report", document, StringComparison.Ordinal);
                Assert.Contains("--evidence <proof-id>=<record-id>", document, StringComparison.Ordinal);
                Assert.Contains("16", document, StringComparison.Ordinal);
                Assert.Contains("hosted-required", document, StringComparison.Ordinal);
                Assert.Contains("structural_state=complete", document, StringComparison.Ordinal);
            }

            if (language == "en")
            {
                var normalizedLedger = string.Join(' ', ledger.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                Assert.Contains("read-only", reference, StringComparison.Ordinal);
                Assert.Contains("does not change", reference, StringComparison.Ordinal);
                Assert.Contains("does not authenticate", orchestration, StringComparison.Ordinal);
                Assert.Contains("This G867 summary alone does not gate worker completion", normalizedLedger, StringComparison.Ordinal);
                Assert.Contains("G868 is a separate explicit", normalizedLedger, StringComparison.Ordinal);
                Assert.Contains("advisory. `complete` requires `structural_state=complete`", normalizedLedger, StringComparison.Ordinal);
                Assert.DoesNotContain("The optional `structural_state=complete`", ledger, StringComparison.Ordinal);
                Assert.Contains("exits 0 only for complete", reference, StringComparison.Ordinal);
                Assert.Contains("exits 1 for incomplete, unavailable, or invalid states", orchestration, StringComparison.Ordinal);
                Assert.Contains("exits 1 for incomplete, unavailable, or invalid states", ledger, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("read-only", reference, StringComparison.Ordinal);
                Assert.Contains("変更しません", reference, StringComparison.Ordinal);
                Assert.Contains("認証", orchestration, StringComparison.Ordinal);
                Assert.Contains("G868", ledger, StringComparison.Ordinal);
                Assert.Contains("worker completion を gate せず", ledger, StringComparison.Ordinal);
                Assert.Contains("exit 0", reference, StringComparison.Ordinal);
                Assert.Contains("exit 1", orchestration, StringComparison.Ordinal);
                Assert.Contains("exit 1", ledger, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void G868OptInGateIsReachableFromAllGuideRoutesAndSixDocs()
    {
        const string marker = "--require-verification";
        const string unit = "--verification-unit";
        const string domain = "--verification-domain";
        const string team = "--verification-team";
        const string head = "--verification-head-sha";
        const string packet = "--verification-packet-sha256";
        const string evidence = "--evidence <proof-id>=<record-id>";
        const string firstOutcome = "issue/pr-created";
        const string secondOutcome = "pr/repair-pushed";
        const string fullGateExample = "intent-cli worker complete --kind issue --number <issue> --outcome pr-created --pr <target-pr> --repo <owner/repo> --require-verification --verification-unit <unit> --verification-domain <domain> --verification-team <team> --verification-head-sha <40-lowerhex> --verification-packet-sha256 <64-lowerhex> --evidence P1=E1 --dry-run --format json";
        foreach (var format in new[] { "json", "markdown" })
        {
            using var catalogWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "commands", "list", "--format", format], CreateContext(), catalogWriter));
            string workerPurpose;
            if (format == "json")
            {
                using var catalog = JsonDocument.Parse(catalogWriter.ToString());
                workerPurpose = catalog.RootElement.GetProperty("groups").EnumerateArray()
                    .Single(group => group.GetProperty("name").GetString() == "worker")
                    .GetProperty("purpose").GetString()!;
            }
            else workerPurpose = catalogWriter.ToString();

            using var soloWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "solo-conductor", "--format", format], CreateContext(), soloWriter));
            string soloInstruction;
            if (format == "json")
            {
                using var solo = JsonDocument.Parse(soloWriter.ToString());
                soloInstruction = solo.RootElement.GetProperty("loop").EnumerateArray()
                    .Single(item => item.GetProperty("id").GetString() == "packet-draft-and-validate")
                    .GetProperty("instruction").GetString()!;
            }
            else soloInstruction = soloWriter.ToString();

            using var workflowWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "workflow", "task", "issue-publish", "--format", format], CreateContext(), workflowWriter));
            string workflowInvariants;
            if (format == "json")
            {
                using var workflow = JsonDocument.Parse(workflowWriter.ToString());
                workflowInvariants = string.Join(" ", workflow.RootElement.GetProperty("invariants").EnumerateArray()
                    .Select(item => item.GetString()));
            }
            else workflowInvariants = workflowWriter.ToString();

            foreach (var route in new[] { workerPurpose, soloInstruction, workflowInvariants })
            {
                Assert.Contains(marker, route, StringComparison.Ordinal);
                Assert.Contains(unit, route, StringComparison.Ordinal);
                Assert.Contains(domain, route, StringComparison.Ordinal);
                Assert.Contains(team, route, StringComparison.Ordinal);
                Assert.Contains(head, route, StringComparison.Ordinal);
                Assert.Contains(packet, route, StringComparison.Ordinal);
                Assert.Contains(evidence, route, StringComparison.Ordinal);
                Assert.Contains(firstOutcome, route, StringComparison.Ordinal);
                Assert.Contains(secondOutcome, route, StringComparison.Ordinal);
                Assert.Contains("--github-only", route, StringComparison.Ordinal);
                Assert.Contains("G785", route, StringComparison.Ordinal);
                Assert.Contains("non-atomic", route, StringComparison.Ordinal);
                Assert.Contains(fullGateExample, route, StringComparison.Ordinal);
                Assert.Contains("worker result-summary --verification-report ... --verification-evidence P1=E1", route, StringComparison.Ordinal);
                Assert.Contains("--verification-evidence", route, StringComparison.Ordinal);
                Assert.Contains("is distinct", route, StringComparison.Ordinal);
                Assert.DoesNotContain("worker enforcement remains open", route, StringComparison.OrdinalIgnoreCase);
            }
        }

        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            foreach (var documentName in new[] { "08-command-reference.md", "12-agent-message-orchestration.md", "1.0-compatibility-ledger.md" })
            {
                var document = File.ReadAllText(Path.Combine(root, "docs", language, documentName));
                Assert.Contains("G868", document, StringComparison.Ordinal);
                Assert.Contains(marker, document, StringComparison.Ordinal);
                Assert.Contains(unit, document, StringComparison.Ordinal);
                Assert.Contains(domain, document, StringComparison.Ordinal);
                Assert.Contains(team, document, StringComparison.Ordinal);
                Assert.Contains(head, document, StringComparison.Ordinal);
                Assert.Contains(packet, document, StringComparison.Ordinal);
                Assert.Contains(evidence, document, StringComparison.Ordinal);
                Assert.Contains(firstOutcome, document, StringComparison.Ordinal);
                Assert.Contains(secondOutcome, document, StringComparison.Ordinal);
                Assert.Contains("--github-only", document, StringComparison.Ordinal);
                Assert.Contains("G785", document, StringComparison.Ordinal);
                Assert.Contains("#1866", document, StringComparison.Ordinal);
            }
        }
    }

    private static void AssertPrivacyNoteFragments(string text, string first, string last)
    {
        Assert.Contains(first, text, StringComparison.Ordinal);
        Assert.Contains(last, text, StringComparison.Ordinal);
    }

    [Fact]
    public void SoloConductorGuideRouteAndCommandCatalogExposeTheRulingReadFlow()
    {
        using var catalogWriter = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(["guide", "commands", "list", "--format", "json"], CreateContext(), catalogWriter));
        using var catalog = JsonDocument.Parse(catalogWriter.ToString());
        Assert.Contains(catalog.RootElement.GetProperty("groups").EnumerateArray(), group =>
            group.GetProperty("name").GetString() == "ruling"
            && group.GetProperty("purpose").GetString()!.Contains("Local immutable operator-ruling", StringComparison.Ordinal));
        var packetPurpose = catalog.RootElement.GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("name").GetString() == "packet")
            .GetProperty("purpose").GetString()!;
        Assert.Contains("packet validate-sources", packetPurpose, StringComparison.Ordinal);
        Assert.Contains("config-independent", packetPurpose, StringComparison.Ordinal);

        const string prerequisite = "Start from a bug chain or from an operator ruling recorded in the host; do not author a packet without one.";
        const string previewCommand = "intent-cli ruling record --id <id> --domain <domain> --team <team> --from-file <ruling.json> --authority-role operator --format json";
        const string writeCommand = "intent-cli ruling record --id <id> --domain <domain> --team <team> --from-file <ruling.json> --authority-role operator --write --format json";
        const string showCommand = "intent-cli ruling show <id> --domain <domain> --team <team> --format json";
        const string validateCommand = "intent-cli ruling validate <id> --domain <domain> --team <team> --format json";
        foreach (var format in new[] { "json", "markdown" })
        {
            using var soloWriter = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "solo-conductor", "--format", format], CreateContext(), soloWriter));
            if (format == "json")
            {
                using var solo = JsonDocument.Parse(soloWriter.ToString());
                Assert.Equal("intent-cli guide solo-conductor", solo.RootElement.GetProperty("route").GetString());
                Assert.Contains("architect", solo.RootElement.GetProperty("model").GetProperty("summary").GetString(), StringComparison.Ordinal);
                Assert.Contains("orchestrator", solo.RootElement.GetProperty("model").GetProperty("summary").GetString(), StringComparison.Ordinal);
                var rulingStep = solo.RootElement.GetProperty("loop").EnumerateArray()
                    .Single(step => step.GetProperty("id").GetString() == "bug-chain-or-ruling");
                var instruction = rulingStep.GetProperty("instruction").GetString()!;
                Assert.StartsWith(prerequisite, instruction, StringComparison.Ordinal);
                Assert.Contains("local immutable record only", instruction, StringComparison.Ordinal);
                Assert.Contains("operator authority is not authenticated", instruction, StringComparison.Ordinal);
                Assert.Contains("does not satisfy packet publication, unit-status, approval, or release gates", instruction, StringComparison.Ordinal);
                Assert.Contains("Commit and plain-push the artifact separately", instruction, StringComparison.Ordinal);
                Assert.Contains("verify it from a fresh clone", instruction, StringComparison.Ordinal);
                var commands = rulingStep.GetProperty("commands").EnumerateArray()
                    .Select(command => command.GetProperty("command").GetString()!).ToArray();
                Assert.True(Array.IndexOf(commands, previewCommand) >= 0);
                Assert.True(Array.IndexOf(commands, writeCommand) > Array.IndexOf(commands, previewCommand));
                Assert.True(Array.IndexOf(commands, showCommand) > Array.IndexOf(commands, writeCommand));
                Assert.True(Array.IndexOf(commands, validateCommand) > Array.IndexOf(commands, showCommand));

                var packetStep = solo.RootElement.GetProperty("loop").EnumerateArray()
                    .Single(step => step.GetProperty("id").GetString() == "packet-draft-and-validate");
                var packetInstruction = packetStep.GetProperty("instruction").GetString()!;
                Assert.Contains("packet validate-sources", packetInstruction, StringComparison.Ordinal);
                Assert.Contains("`review cross-runtime request`, `record`, and `status` commands also revalidate and report the declared local source proof", packetInstruction, StringComparison.Ordinal);
                Assert.Contains("`issue publish-flow` is the external issue create/publish boundary and rechecks these pins before mutation", packetInstruction, StringComparison.Ordinal);
                Assert.Contains("repeat design review after the packet digest changes", packetInstruction, StringComparison.Ordinal);
                Assert.Contains(packetStep.GetProperty("commands").EnumerateArray(), command =>
                    command.GetProperty("command").GetString() == "intent-cli packet validate-sources --execution-unit <unit> --format json");
            }
            else
            {
                var markdown = soloWriter.ToString();
                Assert.Contains(prerequisite, markdown, StringComparison.Ordinal);
                Assert.Contains("local immutable record only", markdown, StringComparison.Ordinal);
                Assert.Contains("operator authority is not authenticated", markdown, StringComparison.Ordinal);
                Assert.Contains("does not satisfy packet publication, unit-status, approval, or release gates", markdown, StringComparison.Ordinal);
                Assert.Contains("Commit and plain-push the artifact separately", markdown, StringComparison.Ordinal);
                Assert.Contains("verify it from a fresh clone", markdown, StringComparison.Ordinal);
                var previewIndex = markdown.IndexOf(previewCommand, StringComparison.Ordinal);
                var writeIndex = markdown.IndexOf(writeCommand, StringComparison.Ordinal);
                var showIndex = markdown.IndexOf(showCommand, StringComparison.Ordinal);
                var validateIndex = markdown.IndexOf(validateCommand, StringComparison.Ordinal);
                Assert.True(previewIndex >= 0 && writeIndex > previewIndex && showIndex > writeIndex && validateIndex > showIndex);
                Assert.Contains("packet-draft-and-validate", markdown, StringComparison.Ordinal);
                Assert.Contains("packet validate-sources", markdown, StringComparison.Ordinal);
                Assert.Contains("`review cross-runtime request`, `record`, and `status` commands also revalidate and report the declared local source proof", markdown, StringComparison.Ordinal);
                Assert.Contains("`issue publish-flow` is the external issue create/publish boundary and rechecks these pins before mutation", markdown, StringComparison.Ordinal);
                Assert.Contains("repeat design review after the packet digest changes", markdown, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Execute_ReviewCatalog_PinsRuntimeDependentModelAndNewOptions_G842()
    {
        var purpose = GuideCommandsListCommand.Groups.Single(group => group.Name == "review").Purpose;

        Assert.Contains("`--model` is required for `copilot` and `opencode` on request and record (both kinds)", purpose, StringComparison.Ordinal);
        Assert.Contains("remains optional for `codex`, `claude`, and `cursor`", purpose, StringComparison.Ordinal);
        Assert.Contains("optional `--effort`", purpose, StringComparison.Ordinal);
        Assert.Contains("request-only optional `--opencode-provider-config`", purpose, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_EmitsClassifiedGroupArray()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var groups = document.RootElement.GetProperty("groups");
        Assert.True(groups.GetArrayLength() >= 11);

        var byName = groups.EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e);

        Assert.Equal("primary", byName["guide"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["intent"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["interview"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["packet"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["worker"].GetProperty("classification").GetString());

        Assert.Equal("support", byName["automation"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["metadata"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["review"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["closeout"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["issue"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["queue"].GetProperty("classification").GetString());

    }

    [Fact]
    public void Execute_RoutedThroughGuideCommandsDispatcher_Works()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsCommand.Execute(
            CreateContext(),
            ["list", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("groups").GetArrayLength() >= 11);
    }

    [Fact]
    public void Execute_GuideCommandsUnknownSubcommand_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsCommand.Execute(
            CreateContext(),
            ["explore"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown 'guide commands' subcommand 'explore'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GuideCommandsMissingSubcommand_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("guide commands requires a subcommand", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            ["--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide commands list", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GroupsRegistry_CoversEveryClassificationValue()
    {
        var classifications = GuideCommandsListCommand.Groups
            .Select(g => g.Classification)
            .Distinct()
            .ToHashSet();

        Assert.Contains("primary", classifications);
        Assert.Contains("support", classifications);
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
