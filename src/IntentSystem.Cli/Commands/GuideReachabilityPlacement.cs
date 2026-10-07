using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Enforces the packet-root placement of the optional G645 guide-reachability
/// declaration. This deliberately checks only the direct child key under
/// <c>implementation_issue_packet</c>; it is not an unknown-key or recursive
/// reserved-key validator.
/// </summary>
internal static class GuideReachabilityPlacement
{
    public const string ErrorMessage =
        "Packet field 'implementation_issue_packet.guide_reachability' is misplaced; "
        + "declare it at the packet root as 'guide_reachability'.";

    /// <summary>
    /// Returns the misplaced declaration key node when the parsed root mapping
    /// contains the direct nested declaration. Presence alone is invalid,
    /// including null and blank values, so callers can report the key's source
    /// location without inspecting or flattening its value.
    /// </summary>
    public static bool TryFindMisplacedDeclarationKey(YamlMappingNode root, out YamlNode? keyNode)
    {
        ArgumentNullException.ThrowIfNull(root);
        keyNode = null;

        if (!root.Children.TryGetValue(new YamlScalarNode("implementation_issue_packet"), out var implementationNode)
            || implementationNode is not YamlMappingNode implementationMapping)
        {
            return false;
        }

        foreach (var (candidate, _) in implementationMapping.Children)
        {
            if (candidate is YamlScalarNode { Value: "guide_reachability" })
            {
                keyNode = candidate;
                return true;
            }
        }

        return false;
    }
}
