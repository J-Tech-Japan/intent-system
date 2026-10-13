# G868 no-marker baseline goldens

These byte-for-byte outputs and provider budgets were captured from the unmodified baseline commit `0bb274e5d3b77a80370b118c53ce307c9fec57d2`. The one-shot test harness was applied only in a detached scratch worktree and was not committed. Its patch SHA-256 and exact capture command are recorded in `no-marker-base-goldens.json`; the original capture TRX is `/private/tmp/g868-base-golden-logs/base-capture.trx`. The fixture stores raw UTF-8 output bytes as base64 along with output SHA-256 and exact provider/mutator counts.

Cases cover issue/PR, dry-run/write, JSON/text, the existing G785 analyzer refusal, already-created dry-run refusal, and the legacy unsupported-Markdown error. These are opt-out compatibility baselines: opted-in gate assertions are tested separately.
