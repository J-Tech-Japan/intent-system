# intent-system

`intent-system` is the npm distribution entry point for `intent-cli`. It
installs the matching self-contained release binary through an npm optional
dependency for macOS Apple Silicon, Linux x64, or Windows x64.

For a persistent command:

```bash
npm install -g intent-system
intent-cli --version
```

For a one-shot invocation:

```bash
npx intent-system guide onboarding
```

The thin shim never installs packages and has no `postinstall` download hook.
When npx is used and no `intent-cli` is already on `PATH`, it prints one short
line suggesting the global install after the command completes. The release
pipeline writes a checksum for every platform package and verifies package,
binary, and `--version` identity before publishing.
