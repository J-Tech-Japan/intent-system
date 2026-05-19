using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// G367: post-pack verifier that opens the .nupkg, locates the
// IntentSystem.Cli.dll inside its tools folder, and walks the assembly's
// AssemblyMetadata entries to confirm the four `PrivatePreview*` keys
// are present (preview pack) or absent (source pack). Using
// System.Reflection.Metadata lets the verifier inspect a packaged .NET
// 10 assembly without resolving its project references, so a .NET 10
// runtime is the only host requirement.
//
// Usage:
//   PreviewArtifactVerify <nupkg-path> --expect-preview
//   PreviewArtifactVerify <nupkg-path> --expect-no-preview
//
// Exit codes:
//   0 -- expectation matched (and printed key/value summary)
//   1 -- usage error
//   2 -- expectation mismatch (preview metadata present when not
//        expected, or absent/incomplete when expected)
//
// The verifier intentionally avoids `Assembly.LoadFrom` so a future
// CLI dependency change cannot accidentally fail this gate via a
// missing transitive dependency. Only the `IntentSystem.Cli` PE file's
// AssemblyMetadata custom attributes are inspected.

const int ExitOk = 0;
const int ExitUsage = 1;
const int ExitMismatch = 2;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: PreviewArtifactVerify <nupkg-path> --expect-preview|--expect-no-preview");
    return ExitUsage;
}

var nupkgPath = args[0];
var mode = args[1];
if (!File.Exists(nupkgPath))
{
    Console.Error.WriteLine($"nupkg not found: {nupkgPath}");
    return ExitUsage;
}

bool expectPreview = mode switch
{
    "--expect-preview" => true,
    "--expect-no-preview" => false,
    _ => throw new ArgumentException($"unknown expectation: {mode}"),
};

using var archive = ZipFile.OpenRead(nupkgPath);
var dllEntry = archive.Entries
    .FirstOrDefault(e =>
        e.FullName.StartsWith("tools/", StringComparison.OrdinalIgnoreCase)
        && e.FullName.EndsWith("/IntentSystem.Cli.dll", StringComparison.OrdinalIgnoreCase));

if (dllEntry is null)
{
    Console.Error.WriteLine($"could not locate tools/<tfm>/<rid>/IntentSystem.Cli.dll in {nupkgPath}");
    return ExitMismatch;
}

using var dllStream = dllEntry.Open();
using var memory = new MemoryStream();
dllStream.CopyTo(memory);
memory.Position = 0;

using var peReader = new PEReader(memory);
var metadataReader = peReader.GetMetadataReader();
var entries = ReadAssemblyMetadata(metadataReader);

var previewKeys = new[]
{
    "PrivatePreviewChannel",
    "PrivatePreviewBuildTimestamp",
    "PrivatePreviewExpiresAt",
    "PrivatePreviewSourceCommit",
};

var present = entries
    .Where(e => previewKeys.Contains(e.Key, StringComparer.Ordinal))
    .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

Console.WriteLine($"nupkg: {nupkgPath}");
Console.WriteLine($"assembly entry: {dllEntry.FullName}");
foreach (var key in previewKeys)
{
    Console.WriteLine(present.TryGetValue(key, out var value)
        ? $"  {key} = {value}"
        : $"  {key} = <absent>");
}

if (expectPreview)
{
    if (present.Count != previewKeys.Length
        || present.Values.Any(string.IsNullOrWhiteSpace))
    {
        Console.Error.WriteLine("FAIL: expected all four PrivatePreview* AssemblyMetadata entries to be present and non-empty.");
        return ExitMismatch;
    }
    if (!string.Equals(present["PrivatePreviewChannel"], "private-preview", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"FAIL: PrivatePreviewChannel must equal 'private-preview', got '{present["PrivatePreviewChannel"]}'.");
        return ExitMismatch;
    }
    Console.WriteLine("OK: preview pack carries all required PrivatePreview* AssemblyMetadata entries.");
    return ExitOk;
}
else
{
    if (present.Count != 0)
    {
        Console.Error.WriteLine($"FAIL: source pack must NOT carry PrivatePreview* AssemblyMetadata entries, but found {present.Count}.");
        return ExitMismatch;
    }
    Console.WriteLine("OK: source pack carries no PrivatePreview* AssemblyMetadata entries.");
    return ExitOk;
}

static IEnumerable<(string Key, string Value)> ReadAssemblyMetadata(MetadataReader reader)
{
    foreach (var handle in reader.CustomAttributes)
    {
        var attr = reader.GetCustomAttribute(handle);

        // Only assembly-level attributes count.
        if (attr.Parent.Kind != HandleKind.AssemblyDefinition)
        {
            continue;
        }

        var (typeNamespace, typeName) = ResolveAttributeType(reader, attr);
        if (typeNamespace != "System.Reflection" || typeName != "AssemblyMetadataAttribute")
        {
            continue;
        }

        if (TryDecodeAssemblyMetadata(reader, attr, out var key, out var value))
        {
            yield return (key, value);
        }
    }
}

static (string Namespace, string Name) ResolveAttributeType(MetadataReader reader, CustomAttribute attr)
{
    return attr.Constructor.Kind switch
    {
        HandleKind.MemberReference => ResolveMemberReference(reader, (MemberReferenceHandle)attr.Constructor),
        HandleKind.MethodDefinition => ResolveMethodDefinition(reader, (MethodDefinitionHandle)attr.Constructor),
        _ => (string.Empty, string.Empty),
    };
}

static (string Namespace, string Name) ResolveMemberReference(MetadataReader reader, MemberReferenceHandle handle)
{
    var member = reader.GetMemberReference(handle);
    return member.Parent.Kind switch
    {
        HandleKind.TypeReference => GetTypeReferenceName(reader, (TypeReferenceHandle)member.Parent),
        _ => (string.Empty, string.Empty),
    };
}

static (string Namespace, string Name) ResolveMethodDefinition(MetadataReader reader, MethodDefinitionHandle handle)
{
    var method = reader.GetMethodDefinition(handle);
    var type = reader.GetTypeDefinition(method.GetDeclaringType());
    return (reader.GetString(type.Namespace), reader.GetString(type.Name));
}

static (string Namespace, string Name) GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
{
    var type = reader.GetTypeReference(handle);
    return (reader.GetString(type.Namespace), reader.GetString(type.Name));
}

static bool TryDecodeAssemblyMetadata(MetadataReader reader, CustomAttribute attr, out string key, out string value)
{
    key = string.Empty;
    value = string.Empty;
    var blob = reader.GetBlobReader(attr.Value);
    // CustomAttribute blob prolog: 0x0001
    if (blob.RemainingBytes < 2)
    {
        return false;
    }
    if (blob.ReadUInt16() != 0x0001)
    {
        return false;
    }
    key = blob.ReadSerializedString() ?? string.Empty;
    value = blob.ReadSerializedString() ?? string.Empty;
    return true;
}
