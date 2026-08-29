using IntentSystem.Cli.Commands;

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
    public void BothDocs_StateTheResidencyDeliveryContractsSideBySide_G660()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("| recorded residency |", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("`recorded-reader-append`", doc, StringComparison.Ordinal);
            Assert.Contains("`recorded-pane-wake`", doc, StringComparison.Ordinal);
            Assert.Contains("`delivery_basis`", doc, StringComparison.Ordinal);
            Assert.Contains("G641/G657", doc, StringComparison.Ordinal);
            Assert.Contains("G660", doc, StringComparison.Ordinal);
            Assert.Contains("preview", doc, StringComparison.Ordinal);
            Assert.Contains("6-field event history", doc, StringComparison.Ordinal);
        }

        Assert.Contains("durable append to that reader is delivery", en, StringComparison.Ordinal);
        Assert.Contains("reader への永続追記が delivery", ja, StringComparison.Ordinal);
        Assert.Contains("failed reader append remains `delivered: false`", en, StringComparison.Ordinal);
        Assert.Contains("reader append が失敗した場合は", ja, StringComparison.Ordinal);
    }

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
    public void BothDocs_CarryAgentNeutralUnattendedRecipes_AndDenialAwareReady_G617()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        Assert.Contains("### 3a. Unattended-launch recipes (agent-neutral) (G617)", en, StringComparison.Ordinal);
        Assert.Contains("### 3a. unattended 起動レシピ（agent-neutral）(G617)", ja, StringComparison.Ordinal);

        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("--kind copilot", doc, StringComparison.Ordinal);
            Assert.Contains("--allow-all-tools", doc, StringComparison.Ordinal);
            Assert.Contains("--add-dir <role-work-root>", doc, StringComparison.Ordinal);
            Assert.Contains("--add-dir <host-routing-root>", doc, StringComparison.Ordinal);
            Assert.Contains("intent-cli notify report", doc, StringComparison.Ordinal);
            Assert.Contains("--max-autopilot-continues 10", doc, StringComparison.Ordinal);
            Assert.Contains("inline_payload_warning_chars: 4096", doc, StringComparison.Ordinal);
            Assert.Contains("delivery_method: file-backed", doc, StringComparison.Ordinal);
            Assert.Contains("--yolo", doc, StringComparison.Ordinal);
            Assert.Contains("--allow-all-paths", doc, StringComparison.Ordinal);
            Assert.Contains("Continue with limited permissions", doc, StringComparison.Ordinal);
            Assert.Contains("Enable all permissions", doc, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", doc, StringComparison.Ordinal);
            Assert.Contains("post-start interaction", doc, StringComparison.Ordinal);
        }

        Assert.Contains("silently auto-denied", en, StringComparison.Ordinal);
        Assert.Contains("静かに自動拒否", ja, StringComparison.Ordinal);
        Assert.Contains("recipe that stops at the command line is incomplete", en, StringComparison.Ordinal);
        Assert.Contains("command line で止まるレシピは不完全", ja, StringComparison.Ordinal);
        Assert.Contains("default `Enable all permissions` answer is unsafe", en, StringComparison.Ordinal);
        Assert.Contains("`default_is_safe: false`", en, StringComparison.Ordinal);
        Assert.Contains("default の `Enable all permissions` は unsafe", ja, StringComparison.Ordinal);
        Assert.Contains("`default_is_safe: false`", ja, StringComparison.Ordinal);
        Assert.Contains("supervision failure, not a shortcut", en, StringComparison.Ordinal);
        Assert.Contains("supervision failure です", ja, StringComparison.Ordinal);
        Assert.Contains("G556 liveness and notify/delivery semantics are unchanged", en, StringComparison.Ordinal);
        Assert.Contains("G556 の liveness と notify/delivery semantics は変わりません", ja, StringComparison.Ordinal);
        Assert.Contains("out-of-scope action is denied", en, StringComparison.Ordinal);
        Assert.Contains("out-of-scope にした action が拒否", ja, StringComparison.Ordinal);
        Assert.Contains("denial probe unexpectedly succeeds", en, StringComparison.Ordinal);
        Assert.Contains("denial probe が予想に反して", ja, StringComparison.Ordinal);
        Assert.Contains("transcript for denials", en, StringComparison.Ordinal);
        Assert.Contains("transcript で拒否を調べます", ja, StringComparison.Ordinal);

        Assert.Contains("Reference-first dispatch", en, StringComparison.Ordinal);
        Assert.Contains("committed canonical", en, StringComparison.Ordinal);
        Assert.Contains("`review-context.md`", en, StringComparison.Ordinal);
        Assert.Contains("842 characters over 14 lines", en, StringComparison.Ordinal);
        Assert.Contains("G619 owns the transport-layer remedy", en, StringComparison.Ordinal);
        Assert.Contains("Read and execute task envelope: <path>", en, StringComparison.Ordinal);
        Assert.Contains("absent declaration preserves existing inline delivery", en, StringComparison.Ordinal);
        Assert.Contains("never refuses or truncates", en, StringComparison.Ordinal);
        Assert.Contains("broken bracketed-paste", en, StringComparison.Ordinal);
        Assert.Contains("reference-first dispatch", ja, StringComparison.Ordinal);
        Assert.Contains("committed canonical な `review-context.md`", ja, StringComparison.Ordinal);
        Assert.Contains("842 文字・14 行", ja, StringComparison.Ordinal);
        Assert.Contains("transport-layer の remedy は G619 が担当", ja, StringComparison.Ordinal);
        Assert.Contains("Read and execute task envelope: <path>", ja, StringComparison.Ordinal);
        Assert.Contains("宣言がなければ既存の inline delivery をそのまま維持", ja, StringComparison.Ordinal);
        Assert.Contains("refuse も truncate もしません", ja, StringComparison.Ordinal);
        Assert.Contains("broken bracketed-paste state", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_CarryCorrectedGitEnvelopeRuleAndRoutingDecision_G716()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        Assert.Contains("E-versus-F", en, StringComparison.Ordinal);
        Assert.Contains("`.git` is not writable unless `<repo>/.git` is itself a declared root", en, StringComparison.Ordinal);
        Assert.Contains("We weighed both legitimate routes", en, StringComparison.Ordinal);
        Assert.Contains("least privilege", en, StringComparison.Ordinal);
        Assert.DoesNotContain("another `--add-dir` does not make", en, StringComparison.Ordinal);

        Assert.Contains("E-versus-F", ja, StringComparison.Ordinal);
        Assert.Contains("`.git` は `<repo>/.git` 自体が宣言 root でない限り", ja, StringComparison.Ordinal);
        Assert.Contains("両方を比較し", ja, StringComparison.Ordinal);
        Assert.Contains("least privilege", ja, StringComparison.Ordinal);
        Assert.DoesNotContain("別の `--add-dir` を追加しても利用可能にはなりません", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_DescribeTheRegistryLimitedAbsentFieldUpdate_G620()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("topology update-field", doc, StringComparison.Ordinal);
            Assert.Contains("--field delivery_method", doc, StringComparison.Ordinal);
            Assert.Contains("--current absent", doc, StringComparison.Ordinal);
            Assert.Contains("--confirm-update-field", doc, StringComparison.Ordinal);
            Assert.Contains("force flag", doc, StringComparison.Ordinal);
        }

        Assert.Contains("registry initially permits only `delivery_method`", en, StringComparison.Ordinal);
        Assert.Contains("registry が最初に許可するのは `delivery_method` だけ", ja, StringComparison.Ordinal);
        Assert.Contains("arbitrary JSON-path editor", en, StringComparison.Ordinal);
        Assert.Contains("任意の JSON path を編集できない", ja, StringComparison.Ordinal);
        Assert.Contains("stale statement is refused in both directions", en, StringComparison.Ordinal);
        Assert.Contains("古い認識にもとづく指定は両方向で拒否", ja, StringComparison.Ordinal);
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

    [Fact]
    public void BothDocs_CarryTheDesignThreadWorkspaceSupervisionSection_G550()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        Assert.Contains("## Design-thread workspace supervision (keeping the team moving)", en, StringComparison.Ordinal);
        Assert.Contains("## 設計スレッドによるワークスペース監督（チームを動かし続ける）", ja, StringComparison.Ordinal);

        // Granted authority is session-layer only; workflow authority does not move.
        Assert.Contains("**Granted authority — session layer only.**", en, StringComparison.Ordinal);
        Assert.Contains("is not granted and never moves", en, StringComparison.Ordinal);
        Assert.Contains("**granted, not assumed**", en, StringComparison.Ordinal);
        Assert.Contains("**付与された権限 — セッション層のみ。**", ja, StringComparison.Ordinal);
        Assert.Contains("付与対象ではなく、決して動きません", ja, StringComparison.Ordinal);

        // Session lifecycle: graceful drop, one holder, operator-visible confirmation.
        Assert.Contains("**graceful drop**", en, StringComparison.Ordinal);
        Assert.Contains("**one holder per role**", en, StringComparison.Ordinal);
        Assert.Contains("**operator-visible**", en, StringComparison.Ordinal);
        Assert.Contains("**graceful drop**", ja, StringComparison.Ordinal);
        Assert.Contains("**1 ロール 1 保持者**", ja, StringComparison.Ordinal);
        Assert.Contains("**オペレーターに可視**", ja, StringComparison.Ordinal);

        // Three layers with their cadences, and the re-arm rule with its cost.
        foreach (var layer in new[] { "real-time message monitor", "blocking-UI pane scan", "periodic state watchdog" })
        {
            Assert.Contains(layer, en, StringComparison.Ordinal);
        }

        Assert.Contains("sub-minute class", en, StringComparison.Ordinal);
        Assert.Contains("tens-of-minutes class", en, StringComparison.Ordinal);
        Assert.Contains("**5.5 hours**", en, StringComparison.Ordinal);
        Assert.Contains("サブ分オーダー", ja, StringComparison.Ordinal);
        Assert.Contains("数十分オーダー", ja, StringComparison.Ordinal);
        Assert.Contains("**5.5 時間**", ja, StringComparison.Ordinal);
        Assert.Contains("**Re-arm across restarts.**", en, StringComparison.Ordinal);
        Assert.Contains("**再起動をまたいだ re-arm。**", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_CarryBothDialogLists_AndTheBoundarySentence_G550()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // The verified-read rule gates every answer.
        Assert.Contains("**verified-read rule**", en, StringComparison.Ordinal);
        Assert.Contains("only after reading its content from the pane**", en, StringComparison.Ordinal);
        Assert.Contains("**verified-read ルール**", ja, StringComparison.Ordinal);

        // MAY list — four items, each with its verification condition.
        Assert.Contains("**Confirmations of work it itself requested**", en, StringComparison.Ordinal);
        Assert.Contains("**Command approvals verified read-only**", en, StringComparison.Ordinal);
        Assert.Contains("**Trust screens for hooks it itself installed**", en, StringComparison.Ordinal);
        Assert.Contains("**Operator-preauthorized mode changes**", en, StringComparison.Ordinal);
        Assert.Contains("**自分自身が要求した作業の確認**", ja, StringComparison.Ordinal);
        Assert.Contains("**read-only であると検証済みのコマンド承認**", ja, StringComparison.Ordinal);
        Assert.Contains("**自分自身がインストールした hook の trust 画面**", ja, StringComparison.Ordinal);
        Assert.Contains("**オペレーターが事前承認した mode 変更**", ja, StringComparison.Ordinal);

        // MUST-ESCALATE list — four categories, credential/security/permission absolute.
        Assert.Contains("**unreadable or unverifiable**", en, StringComparison.Ordinal);
        Assert.Contains("**destructive or irreversible**", en, StringComparison.Ordinal);
        Assert.Contains("**embed a product or design decision**", en, StringComparison.Ordinal);
        Assert.Contains("**credential, security, and permission waits** — never answerable", en, StringComparison.Ordinal);
        Assert.Contains("**読めない・検証できない**", ja, StringComparison.Ordinal);
        Assert.Contains("**破壊的・不可逆**", ja, StringComparison.Ordinal);
        Assert.Contains("**プロダクト/設計の判断を 含む**", ja, StringComparison.Ordinal);
        Assert.Contains("permission の待ち** — 事前承認の有無にかかわらず回答不可", ja, StringComparison.Ordinal);

        // The boundary sentence, on both mirrors.
        Assert.Contains("**Unsticking a session is not deciding for it.**", en, StringComparison.Ordinal);
        Assert.Contains("**セッションの詰まりを解くことは、そのセッションの代わりに決定することではない。**", ja, StringComparison.Ordinal);

        // Watchdog safety rules apply verbatim on both mirrors.
        Assert.Contains("no duplicate delegation, no clearing a permission prompt", en, StringComparison.Ordinal);
        Assert.Contains("委譲の重複禁止、permission プロンプトの自動クリア禁止", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_CarryTheDesignDecisionHoldContract_G552()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        Assert.Contains("## Design-decision holds and bounded authority", en, StringComparison.Ordinal);
        Assert.Contains("## design 判断による hold と限定された権限", ja, StringComparison.Ordinal);
        Assert.Contains("id=\"design-判断による-hold-と-bounded-authority\"", ja, StringComparison.Ordinal);

        // The clarification-backed hold rule, with the contract-violation
        // sentence that makes an agmsg-only hold a violation rather than a
        // style preference.
        Assert.Contains("records a clarification artifact**", en, StringComparison.Ordinal);
        Assert.Contains("`intent-cli clarify open`", en, StringComparison.Ordinal);
        Assert.Contains("**An agmsg-only hold is a contract violation.**", en, StringComparison.Ordinal);
        Assert.Contains("you are not waiting, you are stalled", en, StringComparison.Ordinal);
        Assert.Contains("clarification artifact を記録**", ja, StringComparison.Ordinal);
        Assert.Contains("**agmsg だけの hold は contract violation です。**", ja, StringComparison.Ordinal);
        Assert.Contains("待っているのではなく停止しています", ja, StringComparison.Ordinal);

        // G552 repair: the paste-ready `clarify open` invocation that persists
        // the real question and its recommendation/evidence in the OPEN
        // artifact, and the explicit no-schema-change statement.
        foreach (var flag in new[] { "--question", "--recommended-answer", "--evidence" })
        {
            Assert.Contains(flag, en, StringComparison.Ordinal);
            Assert.Contains(flag, ja, StringComparison.Ordinal);
        }

        Assert.Contains("can never substitute for the durable record", en, StringComparison.Ordinal);
        Assert.Contains("**No clarification schema change.**", en, StringComparison.Ordinal);
        Assert.Contains("永続的な記録の代わりには決してなりません", ja, StringComparison.Ordinal);
        Assert.Contains("**clarification の schema 変更はありません。**", ja, StringComparison.Ordinal);

        // The refined reviewer hold rule — no untracked third option.
        Assert.Contains("**Reviewer hold rule (refined).**", en, StringComparison.Ordinal);
        Assert.Contains("no third option in which the reviewer simply waits", en, StringComparison.Ordinal);
        Assert.Contains("**reviewer hold ルール(refined)。**", ja, StringComparison.Ordinal);
        Assert.Contains("第 3 の選択肢はありません", ja, StringComparison.Ordinal);

        // The measured cost that motivates the slice is stated on both sides.
        Assert.Contains("**nine hours**", en, StringComparison.Ordinal);
        Assert.Contains("**9 時間**", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_RecordAndResolveDesignJudgmentWaits_G610()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // Both design-boundary surfaces carry the duty and the shipped lifecycle.
        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("judgment-wait open", doc, StringComparison.Ordinal);
            Assert.Contains("--owner design", doc, StringComparison.Ordinal);
            Assert.Contains("judgment-wait query", doc, StringComparison.Ordinal);
            Assert.Contains("judgment-wait resolve", doc, StringComparison.Ordinal);
            Assert.Contains("<design-wait-id>", doc, StringComparison.Ordinal);
        }

        Assert.Contains("opening a judgment-wait record is a duty, not an option", en, StringComparison.Ordinal);
        Assert.Contains("Whoever supplies the judgment **must resolve**", en, StringComparison.Ordinal);
        Assert.Contains("An answered-but-open record is a lie", en, StringComparison.Ordinal);
        Assert.Contains("judgment-wait record を開くことは任意ではなく義務です", ja, StringComparison.Ordinal);
        Assert.Contains("回答した人は、その回答と evidence を添えて同じ record を**必ず解決**", ja, StringComparison.Ordinal);
        Assert.Contains("回答済みで open のままの record は嘘です", ja, StringComparison.Ordinal);

        Assert.Contains("## Role boundary — design authors, orchestrator coordinates", en, StringComparison.Ordinal);
        Assert.Contains("## ロール境界 — design が authoring、orchestrator は coordinate", ja, StringComparison.Ordinal);
        Assert.Contains("## Design traffic-controller playbook", en, StringComparison.Ordinal);
        Assert.Contains("## design traffic-controller プレイブック", ja, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Design-judgment wait recording duty", "design-judgment-wait-recording-duty")]
    [InlineData("ja", "design 判断待ちの記録義務", "design-判断待ちの記録義務")]
    public void DesignJudgmentWaitCrossLink_TargetsItsOwnLanguageHeading_G610(string language, string heading, string anchor)
    {
        var doc = ReadDoc(language);

        Assert.Contains($"### {heading}", doc, StringComparison.Ordinal);
        Assert.Contains($"](#{anchor})", doc, StringComparison.Ordinal);

        var otherLanguageAnchor = language == "en"
            ? "#design-判断待ちの記録義務"
            : "#design-judgment-wait-recording-duty";
        Assert.DoesNotContain(otherLanguageAnchor, doc, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_BoundTheDefaultAuthority_AndKeepSemanticDecisionsWithDesign_G552()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // All four enumerated fact-checkable classes appear on both mirrors —
        // the enumeration is what bounds the authority, so a missing row
        // would silently widen it.
        foreach (var row in new[]
                 {
                     "count and enumeration corrections",
                     "wording corrections that follow from a cited fact",
                     "cross-reference and link corrections",
                     "identifier and metadata mismatches against a canonical source",
                 })
        {
            Assert.Contains(row, en, StringComparison.Ordinal);
        }

        foreach (var row in new[]
                 {
                     "件数・列挙の訂正",
                     "引用された事実から導かれる wording 訂正",
                     "相互参照・リンクの訂正",
                     "canonical source との識別子・メタデータ不一致",
                 })
        {
            Assert.Contains(row, ja, StringComparison.Ordinal);
        }

        // Granted / enumerated / evidence-logged / amendable.
        Assert.Contains("**granted** (never assumed", en, StringComparison.Ordinal);
        Assert.Contains("**evidence-logged**", en, StringComparison.Ordinal);
        Assert.Contains("an unlogged resolution is a violation, not a resolution", en, StringComparison.Ordinal);
        Assert.Contains("buys latency, not finality", en, StringComparison.Ordinal);
        Assert.Contains("**付与される**", ja, StringComparison.Ordinal);
        Assert.Contains("**証拠がログされる**", ja, StringComparison.Ordinal);
        Assert.Contains("ログの無い解決は解決では なく違反", ja, StringComparison.Ordinal);
        Assert.Contains("買うのは レイテンシであって finality ではない", ja, StringComparison.Ordinal);

        // G552 repair: the concrete durable evidence sink, its three sections,
        // and the post-hoc amendment property — on both mirrors.
        Assert.Contains("**The evidence sink is `clarify record --from-file`**", en, StringComparison.Ordinal);
        Assert.Contains("`## Recently Resolved`", en, StringComparison.Ordinal);
        Assert.Contains("intent-cli clarify record --domain <domain> --from-file", en, StringComparison.Ordinal);
        Assert.Contains("adds to the trail rather than erasing what it amends", en, StringComparison.Ordinal);

        Assert.Contains("**evidence の sink は `clarify record --from-file`**", ja, StringComparison.Ordinal);
        Assert.Contains("`## Recently Resolved`", ja, StringComparison.Ordinal);
        Assert.Contains("intent-cli clarify record --domain <domain> --from-file", ja, StringComparison.Ordinal);
        Assert.Contains("trail に追加されるだけで、修正対象を消すことはありません", ja, StringComparison.Ordinal);

        // Semantic exclusion, with the double-check scope explicitly untouched.
        Assert.Contains("**Semantic and product decisions are excluded, absolutely.**", en, StringComparison.Ordinal);
        Assert.Contains("whose scope this contract does not touch", en, StringComparison.Ordinal);
        Assert.Contains("**セマンティック・プロダクトの判断は絶対に除外されます。**", ja, StringComparison.Ordinal);
        Assert.Contains("本 contract はそのスコープに触れません", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_DescribeTheDesignReminderLoop_AndTheDetector_G552()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // Interval class, one-per-interval cap, stop-on-answer, and the
        // operator-app reminder model.
        Assert.Contains("**30–60 minute class**", en, StringComparison.Ordinal);
        Assert.Contains("**at most one reminder per interval per open clarification**", en, StringComparison.Ordinal);
        Assert.Contains("**stopping when it is answered**", en, StringComparison.Ordinal);
        Assert.Contains("**operator app**", en, StringComparison.Ordinal);
        Assert.Contains("finds it in the inbox on resume", en, StringComparison.Ordinal);

        Assert.Contains("**30〜60 分オーダー**", ja, StringComparison.Ordinal);
        Assert.Contains("**open な clarification 1 件につき 1 間隔あたり最大 1 通**", ja, StringComparison.Ordinal);
        Assert.Contains("**回答されたら停止**", ja, StringComparison.Ordinal);
        Assert.Contains("**オペレーターアプリ**", ja, StringComparison.Ordinal);
        Assert.Contains("再開時に inbox で見つけます", ja, StringComparison.Ordinal);

        // The detector both mirrors point at.
        Assert.Contains("`design-decision-pending`", en, StringComparison.Ordinal);
        Assert.Contains("`design-decision-pending`", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_CarryTheCrossProjectIsolationSection_G555()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        Assert.Contains("## Cross-project isolation on a shared machine", en, StringComparison.Ordinal);
        Assert.Contains("## 共有マシン上での cross-project isolation", ja, StringComparison.Ordinal);

        // The premise, and the fact that this narrows objects rather than actions.
        Assert.Contains("**Assume you are not alone on this machine.**", en, StringComparison.Ordinal);
        Assert.Contains("it does not change what you may **do**", en, StringComparison.Ordinal);
        Assert.Contains("**このマシン上にいるのは自分だけではない、と前提してください。**", ja, StringComparison.Ordinal);
        Assert.Contains("**何をして よいか** は変えない", ja, StringComparison.Ordinal);

        // The operator incident that motivates it.
        Assert.Contains("2026-07-29", en, StringComparison.Ordinal);
        Assert.Contains("2026-07-29", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_RequireAttributionBeforeMutation_WithAllFourKeys_G555()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        Assert.Contains("**Attribution before mutation.**", en, StringComparison.Ordinal);
        Assert.Contains("**mutation の前に attribution。**", ja, StringComparison.Ordinal);

        // The four gated mutations, on both mirrors.
        // The four gated mutations, on both mirrors. ReadDoc collapses
        // hard wraps, so these match across the wrap points.
        foreach (var gated in new[]
                 {
                     "injecting keys into a pane",
                     "killing a process",
                     "closing or restructuring a workspace",
                     "removing or rewriting a state file",
                 })
        {
            Assert.Contains(gated, en, StringComparison.Ordinal);
        }

        Assert.Contains("**pane へのキー入力**", ja, StringComparison.Ordinal);
        Assert.Contains("**プロセスの kill**", ja, StringComparison.Ordinal);

        // The four verification keys as table rows.
        foreach (var key in new[] { "workspace label", "pane cwd", "process cwd", "agmsg `(team, role)` file naming" })
        {
            Assert.Contains(key, en, StringComparison.Ordinal);
        }

        Assert.Contains("agmsg の `(team, role)` ファイル命名", ja, StringComparison.Ordinal);
        Assert.Contains("pid ごとに", ja, StringComparison.Ordinal);

        // The read-only default.
        Assert.Contains("**Unverifiable attribution = read-only.**", en, StringComparison.Ordinal);
        Assert.Contains("you may not mutate", en, StringComparison.Ordinal);
        Assert.Contains("**attribution できない場合は read-only。**", ja, StringComparison.Ordinal);
        Assert.Contains("mutate はできません", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_ListTheFourSharedSubstrates_AndTheNonDestructiveRecoveryRule_G555()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // All four substrates on both mirrors — the table is the whole set.
        foreach (var substrate in new[] { "herdr server", "~/.agents/skills/agmsg/run", "codex app-servers", "host repo" })
        {
            Assert.Contains(substrate, en, StringComparison.Ordinal);
        }

        Assert.Contains("herdr server", ja, StringComparison.Ordinal);
        Assert.Contains("~/.agents/skills/agmsg/run", ja, StringComparison.Ordinal);
        Assert.Contains("codex app-server", ja, StringComparison.Ordinal);
        Assert.Contains("host repo", ja, StringComparison.Ordinal);

        // The host-repo row references G548 rather than restating it.
        Assert.Contains("G548", en, StringComparison.Ordinal);
        Assert.Contains("G548", ja, StringComparison.Ordinal);

        // Folder exclusivity carries the G521 reason.
        Assert.Contains("(G521)", en, StringComparison.Ordinal);
        Assert.Contains("(G521)", ja, StringComparison.Ordinal);

        // Non-destructive recovery: preserve theirs, rebuild yours.
        Assert.Contains("**Non-destructive recovery.**", en, StringComparison.Ordinal);
        Assert.Contains("**preserve and set aside**", en, StringComparison.Ordinal);
        Assert.Contains("**rebuild your own fresh**", en, StringComparison.Ordinal);
        Assert.Contains("**Recovery defaults to recreate, not cleanup.**", en, StringComparison.Ordinal);

        Assert.Contains("**非破壊的な復旧。**", ja, StringComparison.Ordinal);
        Assert.Contains("**保全して脇に置きます**", ja, StringComparison.Ordinal);
        Assert.Contains("**自分のものは作り直します**", ja, StringComparison.Ordinal);
        Assert.Contains("**復旧の既定は cleanup ではなく recreate です。**", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_RequireVerifiedLiveness_AfterASettleDelay_G556()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        Assert.Contains("**Verified liveness — a startup report is not readiness.**", en, StringComparison.Ordinal);
        Assert.Contains("**verified liveness — startup report は readiness ではない。**", ja, StringComparison.Ordinal);

        // The load-bearing sentence, on both mirrors.
        Assert.Contains("**A startup report is not readiness.**", en, StringComparison.Ordinal);
        Assert.Contains("Never conclude provisioning on the report alone", en, StringComparison.Ordinal);
        Assert.Contains("**startup report は readiness ではありません。**", ja, StringComparison.Ordinal);
        Assert.Contains("report だけで provisioning を 完了と結論してはいけません", ja, StringComparison.Ordinal);

        // The settle delay and all three post-report checks.
        Assert.Contains("**settle delay**", en, StringComparison.Ordinal);
        Assert.Contains("**The pane still hosts the agent TUI**", en, StringComparison.Ordinal);
        Assert.Contains("**An agmsg ping-pong round trip succeeds**", en, StringComparison.Ordinal);
        Assert.Contains("**For codex, the bridge is armed and the app-server attachment is stable**", en, StringComparison.Ordinal);

        Assert.Contains("**settle delay**", ja, StringComparison.Ordinal);
        Assert.Contains("**pane が依然として agent の TUI をホストしている**", ja, StringComparison.Ordinal);
        Assert.Contains("**agmsg の ping-pong 往復が成功する**", ja, StringComparison.Ordinal);
        Assert.Contains("**codex では bridge が armed で app-server attachment が安定している**", ja, StringComparison.Ordinal);

        // The field incident that motivates it.
        Assert.Contains("**seconds** later", en, StringComparison.Ordinal);
        Assert.Contains("**数秒後**", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_DocumentEarlyDeath_AndTheSharedAppServerDeathMode_G556()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // Early death is a NORMAL mode with a named signature.
        Assert.Contains("**Early death is a normal mode.**", en, StringComparison.Ordinal);
        Assert.Contains("**exiting to a shell prompt**", en, StringComparison.Ordinal);
        Assert.Contains("**transport reset**", en, StringComparison.Ordinal);
        Assert.Contains("do not wait for another report", en, StringComparison.Ordinal);

        Assert.Contains("**early death は normal mode です。**", ja, StringComparison.Ordinal);
        Assert.Contains("**shell プロンプトへ抜ける**", ja, StringComparison.Ordinal);
        Assert.Contains("**transport reset**", ja, StringComparison.Ordinal);
        Assert.Contains("次の report を 待ってはいけません", ja, StringComparison.Ordinal);

        // The shared app-server blast radius, with the attribution pointer.
        Assert.Contains("**Shared app-server death mode.**", en, StringComparison.Ordinal);
        Assert.Contains("**takes down every attached TUI at once**", en, StringComparison.Ordinal);
        Assert.Contains("never act on a process you cannot attribute", en, StringComparison.Ordinal);

        Assert.Contains("**共有 app-server の death mode。**", ja, StringComparison.Ordinal);
        Assert.Contains("**接続している すべての TUI が一斉に落ちます**", ja, StringComparison.Ordinal);
        Assert.Contains("attribute できないプロセスには 手を出さないこと", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void BothDocs_PaneScanLists_AgentAbsent_WithShimRelaunchAndLaunchFlagMode_G556()
    {
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");

        // agent-absent is a named scan target alongside blocking dialogs, in
        // the cadence table and in its own list.
        Assert.Contains("(`agent-absent`)", en, StringComparison.Ordinal);
        Assert.Contains("**What the pane scan is looking for.**", en, StringComparison.Ordinal);
        Assert.Contains("**`agent-absent`** — a shell prompt where an agent should be", en, StringComparison.Ordinal);

        Assert.Contains("(`agent-absent`)", ja, StringComparison.Ordinal);
        Assert.Contains("**pane スキャンが探しているもの。**", ja, StringComparison.Ordinal);
        Assert.Contains("**`agent-absent`** — agent がいるべき場所に shell プロンプトが出ている状態", ja, StringComparison.Ordinal);

        // Recovery: shim relaunch, app-server recreation, full re-verification.
        Assert.Contains("**shim-based relaunch**", en, StringComparison.Ordinal);
        Assert.Contains("**full verified-liveness sequence**", en, StringComparison.Ordinal);
        Assert.Contains("**shim 経由の relaunch**", ja, StringComparison.Ordinal);
        Assert.Contains("**verified-liveness の全手順**", ja, StringComparison.Ordinal);

        // The permission mode goes on the launch flag, because modifier chords
        // are not delivered faithfully by synthetic key injection.
        Assert.Contains("`--permission-mode`", en, StringComparison.Ordinal);
        Assert.Contains("shift+tab are not delivered faithfully", en, StringComparison.Ordinal);
        Assert.Contains("`--permission-mode`", ja, StringComparison.Ordinal);
        Assert.Contains("shift+tab のような modifier chord は忠実に届きません", ja, StringComparison.Ordinal);
    }

    [Fact]
    public void FileBackedPointerWordingAgreesAcrossRecipeAndMirrors_G759()
    {
        const string wording = "Read and execute task envelope: <path>";
        var en = ReadDoc("en");
        var ja = ReadDoc("ja");
        var recipe = AgentLaunchRecipeRegistry.Find("copilot")?.DeliveryMethod;

        Assert.NotNull(recipe);
        Assert.Contains(wording, recipe, StringComparison.Ordinal);
        Assert.Contains(wording, en, StringComparison.Ordinal);
        Assert.Contains(wording, ja, StringComparison.Ordinal);
        Assert.DoesNotContain("Read task envelope: <path>", recipe, StringComparison.Ordinal);
        Assert.DoesNotContain("Read task envelope: <path>", en, StringComparison.Ordinal);
        Assert.DoesNotContain("Read task envelope: <path>", ja, StringComparison.Ordinal);
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
