namespace IntentSystem.Cli.Tests;

/// <summary>
/// G549: the terminal-workspace provisioning guidance ships on two surfaces —
/// the `guide orchestrator-thread` output (pinned in
/// <see cref="GuideOrchestratorThreadCommandTests"/>) and the ja/en
/// `12-agent-message-orchestration.md` docs mirror. These tests pin the docs
/// surface and its ja/en parity so the mirror cannot silently drift away from
/// the guide.
/// </summary>
public sealed class AgentMessageOrchestrationDocsTests
{
    private const string DocRelativePath = "12-agent-message-orchestration.md";

    [Fact]
    public void EnglishDoc_HasTerminalWorkspaceProvisioningSection_WithAllSixElements_G549()
    {
        var doc = ReadDoc("en");

        Assert.Contains("## Terminal-workspace provisioning (building the team)", doc, StringComparison.Ordinal);
        // 1. folders — host-side vs implementation clone sources, never-share + reason.
        Assert.Contains("**1. Role folders — create them when absent.**", doc, StringComparison.Ordinal);
        Assert.Contains("host metadata repo", doc, StringComparison.Ordinal);
        Assert.Contains("`(project, type)`-scoped", doc, StringComparison.Ordinal);
        Assert.Contains("G521", doc, StringComparison.Ordinal);
        // 2. topology — one workspace / one team tab / one pane per role, design outside.
        Assert.Contains("**2. Workspace topology.**", doc, StringComparison.Ordinal);
        Assert.Contains("design thread stays outside** the workspace", doc, StringComparison.Ordinal);
        // 3. launch rules — shim reason + direct-spawn warning + permission mode + attended first run.
        Assert.Contains("**3. Launch rules.**", doc, StringComparison.Ordinal);
        Assert.Contains("`codex()` shell shim is what arms the agmsg monitor bridge", doc, StringComparison.Ordinal);
        Assert.Contains("exec's the canonical executable directly bypasses it", doc, StringComparison.Ordinal);
        Assert.Contains("durable", doc, StringComparison.Ordinal);
        // 4. role init — both actas forms, readiness, ping test.
        Assert.Contains("**4. Role initialization.**", doc, StringComparison.Ordinal);
        Assert.Contains("`/agmsg actas <role>`", doc, StringComparison.Ordinal);
        Assert.Contains("`$agmsg actas <role>`", doc, StringComparison.Ordinal);
        Assert.Contains("ping test", doc, StringComparison.Ordinal);
        // 5. exclusivity / handover.
        Assert.Contains("**5. Exclusivity and handover.**", doc, StringComparison.Ordinal);
        Assert.Contains("graceful drop", doc, StringComparison.Ordinal);
        // 6. herdr as the reference manager, internals linked out, substitutable.
        Assert.Contains("**6. herdr is the reference workspace manager.**", doc, StringComparison.Ordinal);
        Assert.Contains("`workspace create`", doc, StringComparison.Ordinal);
        Assert.Contains("`pane split`", doc, StringComparison.Ordinal);
        Assert.Contains("`agent wait`", doc, StringComparison.Ordinal);
        Assert.Contains("does not own, ship, or wrap herdr", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void JapaneseDoc_MirrorsTheProvisioningSection_G549()
    {
        var doc = ReadDoc("ja");

        Assert.Contains("## ターミナルワークスペースの provisioning（チームを構築する）", doc, StringComparison.Ordinal);
        Assert.Contains("host メタデータリポジトリ", doc, StringComparison.Ordinal);
        Assert.Contains("`(project, type)` スコープ", doc, StringComparison.Ordinal);
        Assert.Contains("G521", doc, StringComparison.Ordinal);
        Assert.Contains("`codex()` シェル shim", doc, StringComparison.Ordinal);
        Assert.Contains("`/agmsg actas <role>`", doc, StringComparison.Ordinal);
        Assert.Contains("`$agmsg actas <role>`", doc, StringComparison.Ordinal);
        Assert.Contains("graceful drop", doc, StringComparison.Ordinal);
        Assert.Contains("herdr", doc, StringComparison.Ordinal);
        Assert.Contains("`agent wait`", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_SeparateDeliveryConfigFromLiveAttachment_AndKeepPingAckSoleProof_G549()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // G549 repair: a delivery mode proves configuration only.
        Assert.Contains("proves registration and configuration only", en, StringComparison.Ordinal);
        Assert.Contains("登録と設定を証明するだけです", ja, StringComparison.Ordinal);

        // Live-attachment evidence is agent-specific on both mirrors.
        foreach (var marker in new[] { "Monitor(agmsg inbox stream)", "`1 monitor`", "`1 shell`", "Codex bridge: <team>/<role> alive (pid N)" })
        {
            Assert.Contains(marker, en, StringComparison.Ordinal);
            Assert.Contains(marker, ja, StringComparison.Ordinal);
        }

        // Ping/ack stays the sole end-to-end proof on both mirrors.
        Assert.Contains("ack is the **only** end-to-end proof", en, StringComparison.Ordinal);
        Assert.Contains("ack が **唯一の** end-to-end の証明です", ja, StringComparison.Ordinal);

        // Round-2 repair: the separation holds in both directions — launch-UI
        // state (a trust screen) does not erase delivery configuration.
        Assert.Contains("not live-attached and not session-active**", en, StringComparison.Ordinal);
        Assert.Contains("Launch-UI state never erases configuration, and configuration never implies attachment", en, StringComparison.Ordinal);
        Assert.Contains("session-active でもありません**", ja, StringComparison.Ordinal);
        Assert.Contains("起動 UI の状態が設定を消すことはなく", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_CarryTheAuthorityBoundary_G549()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // G549 repair: unsticking is not deciding — read-first, explicit
        // authorization only, escalate credential/security/permission prompts.
        Assert.Contains("Authority boundary — unsticking is not deciding.", en, StringComparison.Ordinal);
        Assert.Contains("only on pane contents it has actually read", en, StringComparison.Ordinal);
        Assert.Contains("権限境界 — 詰まりを解くことは決定することではない。", ja, StringComparison.Ordinal);
        Assert.Contains("実際に読んだ pane の内容に", ja, StringComparison.Ordinal);

        // Round-2 repair: authorization reaches read-pane trust/allowlist cases
        // only; credential/security/permission prompts are absolutely never
        // answerable by design — on both mirrors.
        Assert.Contains("only to read-pane trust/allowlist cases**", en, StringComparison.Ordinal);
        Assert.Contains("own hook-trust case", en, StringComparison.Ordinal);
        Assert.Contains("permission prompts are **never** answerable by the design thread", en, StringComparison.Ordinal);
        Assert.Contains("**always** escalated to the operator", en, StringComparison.Ordinal);
        Assert.Contains("no authorization makes them answerable", en, StringComparison.Ordinal);

        Assert.Contains("読んだ pane の trust/allowlist ケースに限られます**", ja, StringComparison.Ordinal);
        Assert.Contains("hook-trust ケースです", ja, StringComparison.Ordinal);
        Assert.Contains("設計スレッドが回答することは **決してありません**", ja, StringComparison.Ordinal);
        Assert.Contains("どんな認可もこれらを回答可能にはしません", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_KeepTheSixNumberedProvisioningElements_G549()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // Parity is structural: both mirrors carry the same six numbered
        // elements, so a future edit to one side is visible as a test failure
        // rather than as silent drift.
        foreach (var marker in new[] { "**1.", "**2.", "**3.", "**4.", "**5.", "**6." })
        {
            Assert.Contains(marker, en, StringComparison.Ordinal);
            Assert.Contains(marker, ja, StringComparison.Ordinal);
        }

        // herdr's surfaces are named on both sides and its internals are linked
        // out rather than restated.
        foreach (var surface in new[] { "`workspace create`", "`pane split`", "`agent prompt`", "`agent wait`" })
        {
            Assert.Contains(surface, en, StringComparison.Ordinal);
            Assert.Contains(surface, ja, StringComparison.Ordinal);
        }
    }

    private static string ReadDoc(string language)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "docs", language, DocRelativePath);
            if (File.Exists(candidate))
            {
                // Both mirrors are hard-wrapped and some guidance lives inside
                // blockquotes, so a sentence spans lines and carries `> `
                // continuation markers. Strip the markers and collapse
                // whitespace runs so the assertions pin wording, not wrap points.
                var unwrapped = File.ReadAllLines(candidate)
                    .Select(line => line.TrimStart().TrimStart('>'));

                return string.Join(' ', string.Join('\n', unwrapped)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate docs/{language}/{DocRelativePath} from {AppContext.BaseDirectory}.");
    }
}
