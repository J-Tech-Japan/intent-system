# リリースノート — intent-cli v0.30.0

> **DRAFT / 未リリース。** これは G780 の v0.30.0 contract entry です。tag、GitHub
> Release、package publish、workflow change は作成しません。

## same-repo の claim target (G780)

claim は既存の same-repository topology declaration を尊重するようになりました。呼び出し元
checkout の `.intent-cli/config.toml` に `same_repo_topology = true` と空でない
`metadata_write_branch` の両方がある場合、claim acquire、release、takeover、verify、worker
store probe はすべて `refs/heads/<metadata_write_branch>` を使います。この declaration がない
host は従来どおり G747 の remote default branch を使います。

resolver は fail closed です。metadata write branch が存在しない、または unsafe な場合は
error であり、remote default/current checkout branch へ fallback する許可にはなりません。
`claim stranded` は reverse G763 migration direction も扱います。declared host では remote
default 上の record を `metadata_write_branch` へ transactionally に migrate し、dry-run、
receipt verification、変更されない source branch を保ちます。

G779 の rejected-push classification と fields はどちらの target でも維持されます。このため、
unprotected な `intent-metadata` branch は claim を受け入れ、same-repo declaration がない場合の
protected `main` は正直な `push-rejected` result を返します。

## minor-version justification

これは minor contract change です。既に文書化されている `[project]` topology declaration が
externally observable な claim target を変え、protected product branch で claim を acquire
できなかった same-repository host を有効にするからです。新しい configuration key は追加せず、
default-topology behavior は維持しますが、declared host の canonical claim location を変えるため、
patch-level correction より広い変更です。
