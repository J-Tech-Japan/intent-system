using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G554: the v0.6.1 release-prep deliverable is documentation, so it is pinned
/// the way code is pinned — both language mirrors exist, they cover exactly the
/// two slices that shipped after v0.6.0, they carry the patch rationale and the
/// prepare-only publishing contract, and the post-release version-roll rule is
/// present in the developer reference AND in its closeout checklist. The roll
/// rule is the one a skipped step already broke in the field, so it is pinned
/// on every surface that states it.
/// </summary>
public sealed class ReleaseNotesV061DocsTests
{
    /// <summary>The two slices merged after v0.6.0 — and the only ones this release covers.</summary>
    private static readonly string[] ReleasedSlices = ["G552", "G553"];

    [Fact]
    public void VersionPolicy_RecordsTheReleaseToBeCut_G554()
    {
        // G557: derived from eng/version.json rather than pinned by value —
        // see RepoVersionPolicySource for why a literal pair is the wrong
        // assertion for a field the post-release roll is required to change.
        RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(
            RepoVersionPolicySource.Read());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_CoverExactlyG552AndG553(string language)
    {
        var notes = ReadReleaseNotes(language);

        foreach (var slice in ReleasedSlices)
        {
            Assert.Contains(slice, notes, StringComparison.Ordinal);
        }

        // Exactly these two: no slice from the v0.6.0 batch is re-listed as
        // shipping here.
        foreach (var earlier in new[] { "G549", "G550", "G551" })
        {
            Assert.DoesNotContain($"({earlier})", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_StateThePatchRationale_AndThePrepareOnlyContract(string language)
    {
        var notes = ReadReleaseNotes(language);

        Assert.Contains(language == "en" ? "**patch release**" : "**patch リリース**", notes, StringComparison.Ordinal);
        // The rationale is stated, not merely the label: no new command surface.
        Assert.Contains(
            language == "en" ? "neither slice ships a new command surface" : "どちらのスライスも新しい コマンドサーフェスを出荷しない",
            notes,
            StringComparison.Ordinal);

        Assert.Contains("prepare-only", notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Release-readiness gate (G554)" : "## リリース準備ゲート(G554)",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Publishing v0.6.1" : "## v0.6.1 の publish",
            notes,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_NameTheFieldIncidentsBehindBothSlices(string language)
    {
        var notes = ReadReleaseNotes(language);

        // G552: the nine-hour design hold.
        Assert.Contains(language == "en" ? "**nine hours**" : "**9 時間**", notes, StringComparison.Ordinal);
        // G553: the sekiban #1783 WIP starvation.
        Assert.Contains("#1783", notes, StringComparison.Ordinal);
        Assert.Contains("sekiban-as-a-service", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_CarryThePreviewChannelNoteAboutTheRollRule(string language)
    {
        var notes = ReadReleaseNotes(language);

        // The preview-sorts-below-release incident, its cause, and what a
        // preview-channel user should do about it.
        Assert.Contains("0.6.0-preview", notes, StringComparison.Ordinal);
        Assert.Contains("0.6.2-preview", notes, StringComparison.Ordinal);
        Assert.Contains("dotnet tool update", notes, StringComparison.Ordinal);

        if (language == "en")
        {
            Assert.Contains("sorts **below** the released", notes, StringComparison.Ordinal);
            Assert.Contains("roll `eng/version.json` in a follow-up commit", notes, StringComparison.Ordinal);
            Assert.Contains("not** renumbered retroactively", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("リリース済みの `0.6.0` より **下** にソート", notes, StringComparison.Ordinal);
            Assert.Contains("follow-up commit で `eng/version.json` を roll", notes, StringComparison.Ordinal);
            Assert.Contains("遡って番号を振り直しません", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_CarriesTheRollRule_AndItsCloseoutChecklistStep(string language)
    {
        var reference = ReadDeveloperReference(language);

        Assert.Contains(
            language == "en" ? "### Post-release version roll (G554) — required, immediate" : "### リリース後の version roll(G554) — 必須・即時",
            reference,
            StringComparison.Ordinal);

        // stable = released, next = next patch.
        Assert.Contains(
            language == "en" ? "`stableVersion` = the version just released" : "`stableVersion` = 今リリースした バージョン",
            reference,
            StringComparison.Ordinal);

        // The reason, with the field incident that proves it matters.
        Assert.Contains("2026-07-29", reference, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "sorts **below** its own release version" : "そのリリース バージョンより **下** にソート",
            reference,
            StringComparison.Ordinal);

        // And it is a numbered closeout step, not advice floating next to one.
        Assert.Contains(
            language == "en" ? "**Release closeout checklist** (the roll is step 5" : "**リリース closeout チェックリスト**(roll はステップ 5",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "5. **Roll `eng/version.json` in a follow-up commit**" : "5. **follow-up commit で `eng/version.json` を roll する**",
            reference,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_VersionFlowDoesNotReintroduceTheCurrentPair_G560(string language)
    {
        // The placeholder shape itself is asserted by the shared invariant
        // helper. What is unique here is the NEGATIVE: the example must not
        // quietly regain a worked version pair, which would recreate the second
        // copy of eng/version.json that goes stale on the next roll.
        var reference = ReadDeveloperReference(language);
        var policy = RepoVersionPolicySource.Read();

        var flowStart = reference.IndexOf(
            language == "en" ? "The repository version policy lives in" : "リポジトリのバージョンポリシーは",
            StringComparison.Ordinal);
        var flowEnd = reference.IndexOf(
            language == "en" ? "Post-release version roll" : "リリース後の version roll",
            StringComparison.Ordinal);
        Assert.True(flowStart >= 0 && flowEnd > flowStart, "the developer reference must carry a version-flow section");

        var flowSection = reference[flowStart..flowEnd];
        Assert.DoesNotContain($"\"{policy.NextVersion}\"", flowSection, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{policy.StableVersion}\"", flowSection, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_SatisfiesTheCurrentStateInvariant_G560(string language)
    {
        // G560: the ONE current-state invariant, asserted against the real
        // developer reference and the real eng/version.json. The identical
        // helper runs in the roll simulation below, so "it holds now" and "it
        // holds after a roll" are the same check rather than two drifting
        // copies of one.
        AssertCurrentStateInvariant(ReadDeveloperReference(language), language, RepoVersionPolicySource.Read());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_CarriesNoSupersededGuidance_G560(string language)
    {
        // Version-free negative coverage: wording G554 removed must not return
        // as active guidance, and the closeout cross-reference must name the
        // full step range with what each step carries.
        var reference = ReadDeveloperReference(language);

        Assert.DoesNotContain(
            language == "en" ? "deferred to the NEXT release-prep packet" : "次の release-prep パケットに委ねられます",
            reference,
            StringComparison.Ordinal);

        // Regression for the stale steps 4-5 cross-reference: the readiness
        // closeout must point at steps 5-7 and spell out same-commit stubs, the
        // readiness refresh, and the post-roll green-CI check.
        Assert.DoesNotContain(
            language == "en" ? "per steps 4–5 of the" : "のステップ 4–5 に従います",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "**steps 5–7** of the" : "**ステップ 5–7** に従い",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "DRAFT note stubs in the same commit** (step 5)" : "同一コミットに DRAFT note スタブ**(ステップ 5)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "refreshed to the new line in both language mirrors** (step" : "両ミラーで新しいラインへ更新**(ステップ 6)",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "post-roll green child-main CI check** before the roll counts as" : "roll 後の child main CI green 確認**(ステップ 7)",
            reference,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void RollSimulation_BumpedPolicyPlusRefreshedReadiness_SatisfiesTheSameInvariant_G560(string language)
    {
        // The regression this slice closes is not "four theories were wrong
        // once" — it is that current-state guards FLIP ON EVERY ROLL. So the
        // proof is a roll, driven through the real policy reader against a
        // temporary bumped eng/version.json, and checked with the SAME helper
        // the current-state theory uses. A guard that regains a literal fails
        // here even while it still passes against today's docs.
        AssertRollSimulationHolds(ReadDeveloperReference(language), language, RepoVersionPolicySource.Read());
    }

    /// <summary>
    /// G566: the same simulation, run from the POST-ROLL reality — the live
    /// policy one roll ahead (0.6.2/0.7.0 → 0.7.0/0.7.1 as this lands) with the
    /// readiness sections refreshed to match.
    ///
    /// This is the shape the roller's pre-push verification hit: with
    /// <c>from.StableVersion</c> equal to the hardcoded <c>(0.6.9, 0.7.0)</c>
    /// fixture's <c>nextVersion</c>, the helper's final plain-stable
    /// substitution rewrote the freshly-updated readiness heading back down —
    /// "Expected 0.7.0 / Actual 0.6.9" at the exactly-one-heading assertion.
    /// The repository's own <c>eng/version.json</c> is NOT mutated: the
    /// post-roll policy is derived from the live one and read through the real
    /// reader, so this keeps proving itself after the roll actually lands.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void RollSimulation_HoldsFromThePostRollReality_G566(string language)
    {
        var reference = ReadDeveloperReference(language);
        var policy = RepoVersionPolicySource.Read();

        using var postRollRepo = new TemporaryVersionPolicyRoot(policy.NextVersion, NextPatch(policy.NextVersion));
        var postRollPolicy = RepoVersionPolicySource.ReadFrom(postRollRepo.RootPath);
        var postRollReference = RefreshReadinessForRoll(reference, language, policy, postRollPolicy);

        // The docs as they will read once the roll lands still satisfy the
        // current-state invariant...
        AssertCurrentStateInvariant(postRollReference, language, postRollPolicy);

        // ...and the simulation still holds when run FROM there, which is the
        // theory that failed before this fix.
        AssertRollSimulationHolds(postRollReference, language, postRollPolicy);
    }

    private static void AssertRollSimulationHolds(
        string reference, string language, IntentSystem.Cli.Infrastructure.VersionPolicy from)
    {
        foreach (var (stable, next) in RollFixturePairs(from))
        {
            using var rolledRepo = new TemporaryVersionPolicyRoot(stable, next);
            var rolledPolicy = RepoVersionPolicySource.ReadFrom(rolledRepo.RootPath);
            Assert.Equal(stable, rolledPolicy.StableVersion);
            Assert.Equal(next, rolledPolicy.NextVersion);

            var refreshed = RefreshReadinessForRoll(reference, language, from, rolledPolicy);

            AssertCurrentStateInvariant(refreshed, language, rolledPolicy);
        }
    }

    /// <summary>
    /// G560's pairs, plus the G566 collision pair. The collision is the one the
    /// live policy walks into on a roll: a target whose <c>nextVersion</c>
    /// EQUALS the live <c>stableVersion</c>, so a substitution keyed on
    /// <c>v{from.StableVersion}</c> and one keyed on <c>v{to.NextVersion}</c>
    /// address the same text. It is derived from <paramref name="from"/> rather
    /// than hardcoded, so it keeps reproducing the collision after every future
    /// roll instead of aging into an unrelated pair.
    /// </summary>
    private static IReadOnlyList<(string Stable, string Next)> RollFixturePairs(
        IntentSystem.Cli.Infrastructure.VersionPolicy from) =>
    [
        (from.NextVersion, NextPatch(from.NextVersion)),
        ("0.6.9", "0.7.0"),
        ("0.9.9", "1.0.0"),
        (PreviousVersion(from.StableVersion), from.StableVersion),
    ];

    /// <summary>
    /// G560: the single current-state invariant. Everything it asserts is
    /// derived from <paramref name="policy"/>, and the version-bearing checks
    /// are scoped to the active readiness section so they cannot be satisfied
    /// incidentally by text elsewhere in the file — which is exactly how the
    /// superseded guard passed until a roll exposed it.
    /// </summary>
    private static void AssertCurrentStateInvariant(
        string reference, string language, IntentSystem.Cli.Infrastructure.VersionPolicy policy)
    {
        var headingPrefix = language == "en" ? "### Next release readiness (v" : "### 次リリース準備(v";

        // Exactly ONE active readiness heading, naming the release being cut.
        var headings = reference
            .Split(headingPrefix)
            .Skip(1)
            .Select(chunk => chunk.Split(')')[0])
            .ToArray();
        Assert.Equal(policy.NextVersion, Assert.Single(headings));

        var section = ReadinessSection(reference, language);

        // Section-scoped: the line that just shipped, the notes for the release
        // being cut, the policy pair, and the pack artifact.
        Assert.Contains($"v{policy.StableVersion}", section, StringComparison.Ordinal);
        Assert.Contains($"release-notes-v{policy.NextVersion}.md", section, StringComparison.Ordinal);
        Assert.Contains($"stableVersion {policy.StableVersion}", section, StringComparison.Ordinal);
        Assert.Contains($"nextVersion {policy.NextVersion}", section, StringComparison.Ordinal);
        Assert.Contains($"JTechJapan.IntentSystem.Cli.{policy.NextVersion}.nupkg", section, StringComparison.Ordinal);

        // The version-flow example stays placeholder-based: it is not something
        // a roll has to rewrite, which is the point of the conversion.
        Assert.Contains("\"stableVersion\": \"<stableVersion>\"", reference, StringComparison.Ordinal);
        Assert.Contains("\"nextVersion\": \"<nextVersion>\"", reference, StringComparison.Ordinal);
        Assert.Contains("<nextVersion>-preview.<run>.<attempt>", reference, StringComparison.Ordinal);
        Assert.Contains("<nextPatch>-preview.<run>.<attempt>", reference, StringComparison.Ordinal);
    }

    /// <summary>
    /// G560: rewrites the developer reference the way the roller does at steps
    /// 4-5 — the readiness heading and every current-state mention of the cycle
    /// move to the new line. Deliberately blunt: anything this does not touch
    /// is not current state, and must not be asserting a version.
    ///
    /// G566: the heading is now written LAST, into a slot no substitution can
    /// reach. Previously it was rewritten first and the plain
    /// <c>v{from.StableVersion}</c> substitution ran last, so whenever the live
    /// <c>stableVersion</c> equalled a fixture's <c>nextVersion</c> that final
    /// pass rewrote the fresh heading back down (the live 0.7.0/0.7.1 roll
    /// against the hardcoded <c>(0.6.9, 0.7.0)</c> pair: "Expected 0.7.0 /
    /// Actual 0.6.9"). Reordering alone would fix that one collision; removing
    /// the heading from the string entirely fixes the CLASS, because a
    /// substitution added later cannot reach text that is not there.
    ///
    /// The plain-stable substitution also moves ahead of the ones that
    /// INTRODUCE <c>v{to.NextVersion}</c> text, so it can never re-rewrite a
    /// value this same call just produced.
    /// </summary>
    private static string RefreshReadinessForRoll(
        string reference,
        string language,
        IntentSystem.Cli.Infrastructure.VersionPolicy from,
        IntentSystem.Cli.Infrastructure.VersionPolicy to)
    {
        // Contains no digit, no '.', no 'v' — so it matches none of the
        // substitutions below, whatever versions they are keyed on.
        const string HeadingSlot = "￿_G566_READINESS_HEADING_SLOT_￿";

        var oldHeading = language == "en"
            ? $"### Next release readiness (v{from.NextVersion})"
            : $"### 次リリース準備(v{from.NextVersion})";
        var newHeading = language == "en"
            ? $"### Next release readiness (v{to.NextVersion})"
            : $"### 次リリース準備(v{to.NextVersion})";

        Assert.Contains(oldHeading, reference, StringComparison.Ordinal);

        return reference
            .Replace(oldHeading, HeadingSlot, StringComparison.Ordinal)
            // First: the shipped line moves up. Doing this BEFORE the pair
            // substitutions means it cannot clobber a `v{to.NextVersion}`
            // string that one of them is about to write.
            .Replace($"v{from.StableVersion}", $"v{to.StableVersion}", StringComparison.Ordinal)
            .Replace($"stableVersion {from.StableVersion}", $"stableVersion {to.StableVersion}", StringComparison.Ordinal)
            .Replace($"nextVersion {from.NextVersion}", $"nextVersion {to.NextVersion}", StringComparison.Ordinal)
            .Replace($"JTechJapan.IntentSystem.Cli.{from.NextVersion}.nupkg", $"JTechJapan.IntentSystem.Cli.{to.NextVersion}.nupkg", StringComparison.Ordinal)
            .Replace($"release-notes-v{from.NextVersion}.md", $"release-notes-v{to.NextVersion}.md", StringComparison.Ordinal)
            // Last, and by construction unreachable from every line above.
            .Replace(HeadingSlot, newHeading, StringComparison.Ordinal);
    }

    private static string NextPatch(string version)
    {
        var parts = version.Split('.');
        return $"{parts[0]}.{parts[1]}.{int.Parse(parts[2]) + 1}";
    }

    /// <summary>
    /// G566: the strictly-lower version used to build the collision pair. Only
    /// ever called on a real published <c>stableVersion</c>, which is never
    /// 0.0.0, so the walk down always terminates on a valid version.
    /// </summary>
    private static string PreviousVersion(string version)
    {
        var parts = version.Split('.').Select(int.Parse).ToArray();
        Assert.True(
            parts[0] > 0 || parts[1] > 0 || parts[2] > 0,
            $"cannot derive a lower version than '{version}'.");

        if (parts[2] > 0)
        {
            return $"{parts[0]}.{parts[1]}.{parts[2] - 1}";
        }

        return parts[1] > 0
            ? $"{parts[0]}.{parts[1] - 1}.9"
            : $"{parts[0] - 1}.9.9";
    }

    /// <summary>G560: a temporary repo root holding only a bumped eng/version.json, read through the real policy reader.</summary>
    private sealed class TemporaryVersionPolicyRoot : IDisposable
    {
        public TemporaryVersionPolicyRoot(string stableVersion, string nextVersion)
        {
            RootPath = Directory.CreateTempSubdirectory("g560-roll-simulation-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, "eng"));
            File.WriteAllText(
                Path.Combine(RootPath, "eng", "version.json"),
                $$"""
                {
                  "stableVersion": "{{stableVersion}}",
                  "nextVersion": "{{nextVersion}}"
                }
                """);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void ReleaseNotes_PostReleaseChecklistNotifiesCompletion_RatherThanRequestingPublication(string language)
    {
        var notes = ReadReleaseNotes(language);

        // The checklist is defined as post-Release, so by the time it runs the
        // Release is already published — the item reports completion.
        Assert.Contains(
            language == "en" ? "publication **and** verification of `v0.6.1` are complete" : "`v0.6.1` の publish **および** 検証が完了したことを",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "belongs to the pre-release phase" : "リリース前フェーズに属します",
            notes,
            StringComparison.Ordinal);

        // The stale publish-request wording is gone from the post-release list.
        Assert.DoesNotContain(
            language == "en" ? "Notify the operator to publish the `v0.6.1` GitHub Release" : "オペレーターに `v0.6.1` GitHub Release の publish を通知し",
            notes,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperReference_RollRuleRequiresStubsAndGreenCi_G557(string language)
    {
        var reference = ReadDeveloperReference(language);

        // G557 step 5: the roll commit creates the next-version DRAFT stubs, or
        // it turns main red the moment it lands.
        Assert.Contains(
            language == "en" ? "add DRAFT `docs/{en,ja}/release-notes-v<nextVersion>.md` stubs" : "DRAFT の\n`docs/{en,ja}/release-notes-v<nextVersion>.md` stub を追加する".Replace("\n", " "),
            reference,
            StringComparison.Ordinal);

        // G557 step 7: the roll is not complete until child main CI is green.
        Assert.Contains(
            language == "en" ? "Verify child main CI is green after pushing the roll" : "push 後に child main の CI が green であることを検証する",
            reference,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "the roll is not done until step 7" : "roll はステップ 7 まで終えて初めて完了",
            reference,
            StringComparison.Ordinal);

        // The incident that produced the amendment is recorded with it.
        Assert.Contains("00936844", reference, StringComparison.Ordinal);
        Assert.Contains("G475", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void NextVersionNotes_AreEitherAClearlyMarkedDraftOrRealNotes_G558(string language)
    {
        // G557 created DRAFT stubs at roll time; G558 (release-prep) replaces
        // them with real notes. Both are legal states of the same file, so
        // pinning either one by itself would break the moment the other
        // arrives — the same trap the hardcoded version pair fell into.
        //
        // What must always hold is that the file is unambiguously ONE of them:
        // a stub that says so loudly, or real notes carrying the readiness
        // gate. A file with neither marker is the dangerous middle — it reads
        // like a changelog while nobody authored it.
        var policy = RepoVersionPolicySource.Read();
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, $"release-notes-v{policy.NextVersion}.md");

        Assert.True(File.Exists(path), $"missing next-version notes: docs/{language}/release-notes-v{policy.NextVersion}.md");

        var notes = ReadCollapsed(path);

        var draftBanner = language == "en" ? "**⚠️ DRAFT / UNRELEASED.**" : "**⚠️ DRAFT / 未リリース。**";
        var isDraft = notes.Contains(draftBanner, StringComparison.Ordinal);

        if (isDraft)
        {
            // Stub state: it must refuse to be mistaken for a changelog.
            Assert.Contains(
                language == "en" ? "release-prep packet authors the real content" : "release-prep パケットが author します",
                notes,
                StringComparison.Ordinal);
            Assert.Contains(
                language == "en" ? "must not be treated as a changelog" : "changelog として扱ってはいけません",
                notes,
                StringComparison.Ordinal);
        }
        else
        {
            // Authored state: real notes carry the operator's readiness gate,
            // which is what a release is cut from.
            Assert.Contains(
                language == "en" ? "Release-readiness gate" : "リリース準備ゲート",
                notes,
                StringComparison.Ordinal);
            Assert.Contains(
                language == "en" ? "Publishing v" : " の publish",
                notes,
                StringComparison.Ordinal);
        }

        // Either way the G475 guard's own two requirements hold, so its
        // semantics are unchanged across both states.
        Assert.Contains($"JTechJapan.IntentSystem.Cli --version {policy.NextVersion}", notes, StringComparison.Ordinal);
        Assert.Contains($"releases/tag/v{policy.NextVersion}", notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void V062Notes_CoverExactlyG555G556G557_WithThePatchRationale_G558(string language)
    {
        var notes = ReadCollapsed(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.6.2.md"));

        // Exactly the three merged slices — no missing, no extra.
        foreach (var slice in new[] { "G555", "G556", "G557" })
        {
            Assert.Contains(slice, notes, StringComparison.Ordinal);
        }

        foreach (var earlier in new[] { "(G552)", "(G553)", "(G554)" })
        {
            Assert.DoesNotContain(earlier, notes, StringComparison.Ordinal);
        }

        // The bump rationale is stated, not merely labelled.
        Assert.Contains(language == "en" ? "**patch release**" : "**patch リリース**", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "nothing here changes a CLI surface" : "CLI サーフェスを 変えるものが何もない",
            notes,
            StringComparison.Ordinal);

        // The draft banner is gone — this file is what lifts the stub's own
        // release block.
        Assert.DoesNotContain(
            language == "en" ? "**⚠️ DRAFT / UNRELEASED.**" : "**⚠️ DRAFT / 未リリース。**",
            notes,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void V062Notes_DescribeEachSliceAccurately_G558(string language)
    {
        var notes = ReadCollapsed(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.6.2.md"));

        if (language == "en")
        {
            // G555: the four attribution keys, workspace/folder exclusivity,
            // the substrate table, and the recovery default.
            Assert.Contains("workspace label", notes, StringComparison.Ordinal);
            Assert.Contains("agmsg `(team, role)` file naming", notes, StringComparison.Ordinal);
            Assert.Contains("unverifiable attribution is read-only", notes, StringComparison.Ordinal);
            Assert.Contains("Recovery defaults to recreate, not cleanup.", notes, StringComparison.Ordinal);

            // G556: settle delay + three checks, early death, agent-absent,
            // shared app-server death mode.
            Assert.Contains("settle delay", notes, StringComparison.Ordinal);
            Assert.Contains("Early death is a normal mode", notes, StringComparison.Ordinal);
            Assert.Contains("`agent-absent`", notes, StringComparison.Ordinal);
            Assert.Contains("takes down **every attached TUI at once**", notes, StringComparison.Ordinal);

            // G557: derived assertions, the stub mechanism, the amended rule.
            Assert.Contains("Version-agnostic assertions", notes, StringComparison.Ordinal);
            Assert.Contains("roll-simulation fixture", notes, StringComparison.Ordinal);
            Assert.Contains("draft-stub mechanism", notes, StringComparison.Ordinal);
            Assert.Contains("verify child main CI green after pushing", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("workspace label", notes, StringComparison.Ordinal);
            Assert.Contains("agmsg の `(team, role)` ファイル命名", notes, StringComparison.Ordinal);
            Assert.Contains("read-only", notes, StringComparison.Ordinal);
            Assert.Contains("復旧の既定は cleanup ではなく recreate です。", notes, StringComparison.Ordinal);

            Assert.Contains("settle delay", notes, StringComparison.Ordinal);
            Assert.Contains("early death は normal mode", notes, StringComparison.Ordinal);
            Assert.Contains("`agent-absent`", notes, StringComparison.Ordinal);
            Assert.Contains("attach しているすべての TUI が一斉に落ちます", notes, StringComparison.Ordinal);

            Assert.Contains("version-agnostic な assertion", notes, StringComparison.Ordinal);
            Assert.Contains("roll シミュレーション fixture", notes, StringComparison.Ordinal);
            Assert.Contains("DRAFT stub の仕組み", notes, StringComparison.Ordinal);
            Assert.Contains("child main の CI が green であることを roller が検証", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void V062Notes_KeepThePrepareOnlyContract_AndTheUpgradeSplit_G558(string language)
    {
        var notes = ReadCollapsed(Path.Combine(
            RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.6.2.md"));

        Assert.Contains("prepare-only", notes, StringComparison.Ordinal);
        Assert.Contains("release.yml", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Release-readiness gate (G558)" : "## リリース準備ゲート(G558)",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "## Publishing v0.6.2" : "## v0.6.2 の publish",
            notes,
            StringComparison.Ordinal);

        // The upgrade section separates additive guide surfaces from the
        // corrective release-flow change, so a consumer can tell which applies.
        Assert.Contains(
            language == "en" ? "Additive — guidance only, no action needed" : "追加のみ — ガイダンスであり対応不要",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "Corrective — release-flow only" : "是正的 — リリースフローのみ",
            notes,
            StringComparison.Ordinal);

        // The post-release roll reminder carries the G557 amendment, so the
        // next roll does not repeat the incident this release documents.
        Assert.Contains("0.6.3", notes, StringComparison.Ordinal);
        Assert.Contains(
            language == "en" ? "in the same commit as" : "同じ commit で",
            notes,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareOnlyDiff_AddsNoPublishAutomation_G554()
    {
        // The packet is documentation + version metadata only. Pin that
        // release.yml still triggers on a published Release rather than on the
        // version-bump merge landing.
        var releaseWorkflow = Path.Combine(RepoRoot(), ".github", "workflows", "release.yml");
        Assert.True(File.Exists(releaseWorkflow), $"Expected {releaseWorkflow} to exist.");

        var workflow = File.ReadAllText(releaseWorkflow);
        Assert.Contains("release:", workflow, StringComparison.Ordinal);
        Assert.Contains("published", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// G560: the slice of the developer reference under the ACTIVE readiness
    /// heading, so assertions about that section cannot accidentally be
    /// satisfied by text elsewhere in the file — which is exactly how the
    /// previous guard passed incidentally until a roll exposed it.
    /// </summary>
    private static string ReadinessSection(string reference, string language)
    {
        var heading = language == "en" ? "### Next release readiness (v" : "### 次リリース準備(v";
        var start = reference.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, "the developer reference must carry an active readiness section");

        var end = reference.IndexOf("### ", start + heading.Length, StringComparison.Ordinal);
        return end > start ? reference[start..end] : reference[start..];
    }

    private static string ReadReleaseNotes(string language) =>
        ReadCollapsed(Path.Combine(RepoRoot(), "docs", language, "release-notes-v0.6.1.md"));

    private static string ReadDeveloperReference(string language) =>
        ReadCollapsed(Path.Combine(RepoRoot(), "docs", language, "09-developer-reference.md"));

    private static string ReadCollapsed(string path)
    {
        Assert.True(File.Exists(path), $"Expected {path} to exist.");

        // Both mirrors are hard-wrapped and some guidance lives inside
        // blockquotes, so a sentence spans lines and carries `> ` continuation
        // markers. Strip the markers and collapse whitespace runs so the
        // assertions pin wording, not wrap points.
        var unwrapped = File.ReadAllLines(path)
            .Select(line => line.TrimStart().TrimStart('>'));

        return string.Join(' ', string.Join('\n', unwrapped)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "eng", "version.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
