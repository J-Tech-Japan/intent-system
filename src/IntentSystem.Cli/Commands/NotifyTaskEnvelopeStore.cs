using System.Text;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyTaskEnvelopeWriteResult(bool Written, string Path, string? Error);

internal static class NotifyTaskEnvelopeStore
{
    internal static Func<string, string, NotifyTaskEnvelopeWriteResult>? WriteOverride { get; set; }

    public static NotifyTaskEnvelopeWriteResult Write(
        string routingRoot,
        string domain,
        string team,
        string taskId,
        string nonce,
        string envelope)
    {
        var path = ResolvePath(routingRoot, domain, team, taskId, nonce);
        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, envelope);
        }

        var directory = Path.GetDirectoryName(path)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, envelope, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
            return new NotifyTaskEnvelopeWriteResult(true, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new NotifyTaskEnvelopeWriteResult(false, path, exception.Message);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The primary write result is already fail-closed; a failed
                    // best-effort temporary cleanup must not turn it into a send.
                }
            }
        }
    }

    public static string ResolvePath(string routingRoot, string domain, string team, string taskId, string nonce) =>
        Path.GetFullPath(Path.Combine(
            routingRoot,
            ".intent-cli",
            "tasks",
            domain,
            team,
            $"{taskId}-{nonce}.md"));
}

internal sealed record NotifyTaskEnvelopeDelivery
{
    public const string InlineMethod = "inline";
    public const string FileBackedMethod = "file-backed";

    public required bool Resolved { get; init; }
    public required bool FileBacked { get; init; }
    public required string TransportPayload { get; init; }
    public string? Pointer { get; init; }
    public string? TaskFile { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }
    public string? ResultDeliveryMethod => FileBacked ? FileBackedMethod : null;
    public string? ResultPointer => FileBacked ? Pointer : null;

    public static NotifyTaskEnvelopeDelivery Inline(string payload) => new()
    {
        Resolved = true,
        FileBacked = false,
        TransportPayload = payload,
        Summary = "Inline envelope delivery is selected.",
    };

    public static NotifyTaskEnvelopeDelivery Resolve(NotifyOptions options, string payload)
    {
        var topology = NotifyRoleTopologyStore.Resolve(options.RoutingRoot!, options.Domain!, options.Team!);
        var roleResolution = topology.Resolved && topology.Topology is { } teamTopology
            ? NotifyRoleTopologyStore.ResolveRecordedRole(teamTopology, options.ToRole!)
            : null;
        if (roleResolution?.Resolved != true
            || roleResolution.Record is not { } recipient
            || string.IsNullOrWhiteSpace(recipient.DeliveryMethod))
        {
            return Inline(payload);
        }

        if (string.Equals(recipient.DeliveryMethod, InlineMethod, StringComparison.Ordinal))
        {
            return Inline(payload);
        }

        if (!string.Equals(recipient.DeliveryMethod, FileBackedMethod, StringComparison.Ordinal)
            || !string.Equals(recipient.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
        {
            return new NotifyTaskEnvelopeDelivery
            {
                Resolved = false,
                FileBacked = false,
                TransportPayload = payload,
                Cause = "delivery-method-invalid",
                Summary = $"Recorded delivery_method '{recipient.DeliveryMethod}' for role '{options.ToRole}' is unsupported. "
                    + "Use inline or file-backed for a herdr resident and retry notify.",
            };
        }

        var taskFile = NotifyTaskEnvelopeStore.ResolvePath(
            options.RoutingRoot!, options.Domain!, options.Team!, options.TaskId!, options.ResultNonce!);
        var pointer = $"Read and execute task envelope: {taskFile}";
        return new NotifyTaskEnvelopeDelivery
        {
            Resolved = true,
            FileBacked = true,
            TransportPayload = pointer,
            Pointer = pointer,
            TaskFile = taskFile,
            Summary = "File-backed envelope delivery is selected.",
        };
    }
}
