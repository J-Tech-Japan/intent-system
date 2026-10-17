# G871 detached-base compatibility goldens

`no-marker-base-goldens.json` contains the exact builder/reviewer JSON and Markdown stdout and `--out` bytes captured from detached baseline `3a446fe2dac3925eaef9c2306e4e230ee618383a`. The baseline-only test harness patch is preserved at `/private/tmp/g871-baseline-capture/capture-harness.patch` (SHA-256 `1685b09573fa585dff7989c09c2f7826c2bc216ad05bb161c715b4b057bf4b1e`); it was applied only in a detached scratch worktree and was not committed to that baseline.

The fixture records the exact capture command, harness hash, original TRX path and hash, output bytes/hashes, and per-instance observation callback sequence. No output normalization was needed. The current no-marker tests compare against these baseline bytes and callback sequence rather than comparing two runs of the changed renderer.
