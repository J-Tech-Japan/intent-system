using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace IntentSystem.Cli.Commands;

internal static class CrossRuntimeReviewRequestSupport
{
    private static readonly ConcurrentDictionary<string, Action<string>> BeforeMoveHooks =
        new(StringComparer.Ordinal);

    internal static IDisposable RegisterBeforeMoveHook(string targetPath, Action<string> hook)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(hook);

        var key = Path.GetFullPath(targetPath);
        if (!BeforeMoveHooks.TryAdd(key, hook))
        {
            throw new InvalidOperationException($"a before-move hook is already registered for '{key}'.");
        }

        return new BeforeMoveHookRegistration(key);
    }

    internal static bool TryValidateOutDir(
        string outDir,
        string runtime,
        TextWriter writer,
        string format,
        out string errorCause)
    {
        return TryValidateOutDir(
            outDir,
            runtime,
            CrossRuntimeReviewRecord.KindImplementation,
            true,
            outDir,
            null,
            writer,
            format,
            out errorCause);
    }

    internal static bool TryValidateOutDir(
        string outDir,
        string runtime,
        string kind,
        bool hasClone,
        string workspace,
        string? providerConfigPath,
        TextWriter writer,
        string format,
        out string errorCause)
    {
        errorCause = string.Empty;
        if (File.Exists(outDir))
        {
            RefuseOutDir(writer, format, CrossRuntimeReviewCauses.PathInvalid,
                $"--out-dir '{outDir}' is a file.", "pass a new or empty directory.");
            return false;
        }

        // This is deliberately before Directory.EnumerateFileSystemEntries.
        // The new runtimes must refuse a contained or protected layout with
        // path-invalid, even if the out-dir also has foreign entries. The
        // existing runtimes retain only their pre-existing planned-path
        // safety checks and do not use the new protected-root set.
        var refused = runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode
            ? CrossRuntimeReviewHomeAccessGuard.TryRefuseRequestPaths(
                outDir,
                runtime,
                kind,
                hasClone,
                workspace,
                providerConfigPath,
                out var plannedDetail)
            : CrossRuntimeReviewHomeAccessGuard.TryRefusePlannedPaths(outDir, runtime, out plannedDetail);
        if (refused)
        {
            RefuseOutDir(writer, format, CrossRuntimeReviewCauses.PathInvalid, plannedDetail,
                "pass a disjoint reviewer workspace and out-dir outside operator runtime state.");
            return false;
        }

        if (!Directory.Exists(outDir))
        {
            return true;
        }

        var rendered = CrossRuntimeReviewFiles.RenderedFor(runtime, kind, hasClone);
        string?[] foreign;
        try
        {
            if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode)
            {
                CrossRuntimeReviewHomeAccessGuard.BeforeProtectedPathAccess(outDir);
            }

            foreign = Directory.EnumerateFileSystemEntries(outDir)
                .Select(Path.GetFileName)
                .Where(name => name is not null && !rendered.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (runtime != CrossRuntimeReviewRuntimes.Copilot
                && runtime != CrossRuntimeReviewRuntimes.Opencode)
            {
                throw;
            }

            RefuseOutDir(writer, format, CrossRuntimeReviewCauses.PathInvalid,
                $"--out-dir '{outDir}' could not be inspected: {exception.Message}",
                "pass a readable and writable directory outside operator runtime state.");
            return false;
        }

        if (foreign.Length > 0)
        {
            RefuseOutDir(writer, format, CrossRuntimeReviewCauses.OutDirNotEmpty,
                $"--out-dir '{outDir}' contains other entries: {string.Join(", ", foreign)}.",
                "pass a new or empty directory so a stale verdict can never be mixed with this request.");
            return false;
        }

        foreach (var directoryName in CrossRuntimeReviewRuntimes.IsolationDirectories(runtime)
                     .Concat(rendered.Contains(CrossRuntimeReviewFiles.Workspace, StringComparer.Ordinal)
                         ? [CrossRuntimeReviewFiles.Workspace]
                         : []))
        {
            var path = Path.Combine(outDir, directoryName);
            if (!PathIsPresent(path))
            {
                continue;
            }

            if (runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode)
            {
                CrossRuntimeReviewHomeAccessGuard.BeforeProtectedPathAccess(path);
            }

            try
            {
                var isDirectory = Directory.Exists(path);
                if (!isDirectory || Directory.EnumerateFileSystemEntries(path).Any())
                {
                    RefuseOutDir(writer, format, CrossRuntimeReviewCauses.OutDirNotEmpty,
                        isDirectory
                            ? $"--out-dir '{outDir}' contains a non-empty directory '{directoryName}'."
                            : $"--out-dir '{outDir}' contains a file named '{directoryName}'.",
                        "pass a new or empty directory.");
                    return false;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (runtime != CrossRuntimeReviewRuntimes.Copilot
                    && runtime != CrossRuntimeReviewRuntimes.Opencode)
                {
                    throw;
                }

                RefuseOutDir(writer, format, CrossRuntimeReviewCauses.PathInvalid,
                    $"--out-dir '{outDir}' could not be inspected: {exception.Message}",
                    "pass a readable and writable directory outside operator runtime state.");
                return false;
            }
        }

        return true;
    }

    internal static IReadOnlyList<string> WriteRequestFiles(
        string runtime,
        string kind,
        bool hasClone,
        string workspace,
        string outDir,
        string prompt,
        string schema,
        string invocation,
        string? opencodeProviderConfigPath)
    {
        var refused = runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode
            ? CrossRuntimeReviewHomeAccessGuard.TryRefuseRequestPaths(
                outDir,
                runtime,
                kind,
                hasClone,
                workspace,
                opencodeProviderConfigPath,
                out var plannedDetail)
            : CrossRuntimeReviewHomeAccessGuard.TryRefusePlannedPaths(outDir, runtime, out plannedDetail);
        if (refused)
        {
            throw new InvalidOperationException(plannedDetail);
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var privateWrites = runtime is CrossRuntimeReviewRuntimes.Copilot or CrossRuntimeReviewRuntimes.Opencode;
        Directory.CreateDirectory(outDir);
        WriteTextFile(Path.Combine(outDir, CrossRuntimeReviewFiles.Prompt), prompt, utf8, privateWrites);
        WriteTextFile(Path.Combine(outDir, CrossRuntimeReviewFiles.Schema), schema, utf8, privateWrites);
        WriteTextFile(Path.Combine(outDir, CrossRuntimeReviewFiles.Invocation), invocation, utf8, privateWrites);

        if (privateWrites)
        {
            foreach (var directoryName in CrossRuntimeReviewRuntimes.IsolationDirectories(runtime))
            {
                CreatePrivateDirectory(Path.Combine(outDir, directoryName));
            }
        }

        if (runtime == CrossRuntimeReviewRuntimes.Opencode)
        {
            WriteBytesFile(
                Path.Combine(outDir, CrossRuntimeReviewFiles.OpencodeReviewerConfig),
                CrossRuntimeReviewOpencodeConfig.ReviewerConfigBytes(opencodeProviderConfigPath));
        }

        if (CrossRuntimeReviewFiles.RenderedFor(runtime, kind, hasClone).Contains(CrossRuntimeReviewFiles.Workspace, StringComparer.Ordinal))
        {
            CreatePrivateDirectory(Path.Combine(outDir, CrossRuntimeReviewFiles.Workspace));
        }

        return CrossRuntimeReviewFiles.RenderedFor(runtime, kind, hasClone)
            .Select(name => Path.Combine(outDir, name))
            .ToArray();
    }

    private static void WriteTextFile(string path, string content, UTF8Encoding encoding, bool privateWrite)
    {
        if (privateWrite)
        {
            ReplaceWrite(path, encoding.GetBytes(content));
            return;
        }

        // Preserve the merge-base behavior for codex, claude and cursor.
        File.WriteAllText(path, content, encoding);
    }

    private static void WriteBytesFile(string path, byte[] content) => ReplaceWrite(path, content);

    private static void ReplaceWrite(string path, byte[] content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var mode = ExistingStrictMode(fullPath);
        try
        {
            var options = new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = mode ?? (UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            using (var stream = new FileStream(temp, options))
            {
                stream.Write(content, 0, content.Length);
                stream.Flush(flushToDisk: true);
            }

            InvokeBeforeMoveHook(fullPath, temp);
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static UnixFileMode? ExistingStrictMode(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var existing = File.GetUnixFileMode(path);
            var permissions = existing & (UnixFileMode)0x1FF;
            var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return ((int)permissions & ~((int)privateMode)) == 0 ? permissions : privateMode;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        const UnixFileMode privateDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, privateDirectoryMode);
        // Directory.CreateDirectory(path, mode) preserves an existing directory's
        // mode. An accepted empty isolation directory must still be private.
        File.SetUnixFileMode(path, privateDirectoryMode);
    }

    private static void InvokeBeforeMoveHook(string targetPath, string tempPath)
    {
        if (!BeforeMoveHooks.IsEmpty && BeforeMoveHooks.TryGetValue(targetPath, out var hook))
        {
            hook(tempPath);
        }
    }

    private sealed class BeforeMoveHookRegistration : IDisposable
    {
        private readonly string key;

        public BeforeMoveHookRegistration(string key) => this.key = key;

        public void Dispose() => BeforeMoveHooks.TryRemove(key, out _);
    }

    private static bool PathIsPresent(string path) => CrossRuntimeReviewHomeAccessGuard.PathIsPresent(path);

    private static void RefuseOutDir(TextWriter writer, string format, string cause, string detail, string fix)
    {
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(new CrossRuntimeReviewRefusal
            {
                Command = $"{ReviewCrossRuntimeCommand.CommandName} request",
                Outcome = "refused",
                Cause = cause,
                Detail = detail,
                Fix = fix,
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }));
            return;
        }

        writer.WriteLine($"{ReviewCrossRuntimeCommand.CommandName} request: refused ({cause})");
        writer.WriteLine($"- detail: {detail}");
        writer.WriteLine($"- fix: {fix}");
    }
}
