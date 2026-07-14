namespace IntentSystem.Cli.Commands;

/// <summary>
/// G522: shared domain-resolution order for execution-unit-resolving
/// surfaces (<c>review closeout-plan</c>, <c>automation
/// queue-seed-from-packet</c>, <c>automation publish-recovery</c>, and
/// peers that resolve an execution unit from <c>--pr</c> /
/// <c>--execution-unit</c>). Resolution order when a single execution
/// unit's packet is in view:
///
/// <list type="number">
/// <item>an explicit <c>--domain</c> wins; it is an error if it
///   contradicts the domain declared by the resolved packet.yaml /
///   queue metadata;</item>
/// <item>otherwise the domain declared by the resolved packet.yaml /
///   queue metadata is used;</item>
/// <item>otherwise the surface fails loud, naming candidate domains
///   and the exact re-invocation — it never silently falls back to the
///   host's default domain binding (<c>[project] domain</c>).</item>
/// </list>
///
/// This does not change the default-binding mechanism itself (still
/// used by surfaces outside this resolution order); it only changes
/// what execution-unit-resolving surfaces consult when <c>--domain</c>
/// is omitted.
/// </summary>
internal static class PacketDomainResolution
{
    public const string ReasonContradiction = "domain-contradiction";
    public const string ReasonUnderivable = "domain-underivable";

    public static PacketDomainResolutionResult Resolve(
        string? explicitDomain,
        string? packetDeclaredDomain,
        IReadOnlyList<string> candidateDomains,
        string reinvocationHint)
    {
        ArgumentNullException.ThrowIfNull(candidateDomains);
        ArgumentException.ThrowIfNullOrWhiteSpace(reinvocationHint);

        var normalizedExplicit = string.IsNullOrWhiteSpace(explicitDomain) ? null : explicitDomain!.Trim();
        var normalizedPacket = string.IsNullOrWhiteSpace(packetDeclaredDomain) ? null : packetDeclaredDomain!.Trim();

        if (normalizedExplicit is not null)
        {
            if (normalizedPacket is not null
                && !string.Equals(normalizedExplicit, normalizedPacket, StringComparison.Ordinal))
            {
                return PacketDomainResolutionResult.Error(
                    ReasonContradiction,
                    $"--domain '{normalizedExplicit}' contradicts the domain declared by the resolved packet "
                    + $"('{normalizedPacket}'). Pass the matching --domain, or fix the packet's `domain:` field "
                    + "if it is wrong.");
            }

            return PacketDomainResolutionResult.Ok(normalizedExplicit);
        }

        if (normalizedPacket is not null)
        {
            return PacketDomainResolutionResult.Ok(normalizedPacket);
        }

        var candidates = candidateDomains.Count > 0
            ? string.Join(", ", candidateDomains)
            : "(none found under intents/)";
        return PacketDomainResolutionResult.Error(
            ReasonUnderivable,
            "--domain could not be derived: the resolved packet/queue metadata does not declare a `domain:` field "
            + $"and no --domain was passed. Candidate domains: {candidates}. Re-invoke with: {reinvocationHint}");
    }
}

/// <summary>G522: outcome of <see cref="PacketDomainResolution.Resolve"/>.</summary>
internal readonly record struct PacketDomainResolutionResult
{
    public string? Domain { get; init; }

    public bool IsError { get; init; }

    public string? Reason { get; init; }

    public string? ErrorMessage { get; init; }

    public static PacketDomainResolutionResult Ok(string domain) => new() { Domain = domain };

    public static PacketDomainResolutionResult Error(string reason, string message) => new()
    {
        IsError = true,
        Reason = reason,
        ErrorMessage = message,
    };
}

/// <summary>
/// G522: best-effort candidate-domain enumeration for the fail-loud
/// error message — lists the domain directory names under
/// <c>intents/</c> (parent host root when configured, else the child
/// <see cref="CliContext.RepoRoot"/>), mirroring the parent-aware
/// lookup convention used by <see cref="NextSliceDomainBindingsExecutionUnitRegex"/>.
/// Purely advisory: an empty/missing <c>intents/</c> directory yields
/// an empty list rather than an error.
/// </summary>
internal static class DomainCandidateScanner
{
    public static IReadOnlyList<string> Scan(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var root = context.ResolveParentIntentRepoRootPath();
        if (string.IsNullOrWhiteSpace(root))
        {
            root = context.RepoRoot;
        }
        if (string.IsNullOrWhiteSpace(root))
        {
            return Array.Empty<string>();
        }

        var intentsDirectory = Path.Combine(root, "intents");
        if (!Directory.Exists(intentsDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(intentsDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
