using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G645: the packet declaration that makes guide reachability explicit.  A
/// packet may name one or more guide routes (guide surface, role, and target
/// surface), or explicitly say that it adds no role-facing surface.  Absence
/// is intentionally distinct from that explicit no-surface answer.
/// </summary>
internal sealed record GuideReachabilityRoute
{
    public required string GuideSurface { get; init; }

    public required string Role { get; init; }

    public required string TargetSurface { get; init; }
}

internal sealed record GuideReachabilityDeclaration
{
    public required bool IsDeclared { get; init; }

    public required bool NoRoleFacingSurface { get; init; }

    public required IReadOnlyList<GuideReachabilityRoute> Routes { get; init; }

    public static readonly GuideReachabilityDeclaration Absent = new()
    {
        IsDeclared = false,
        NoRoleFacingSurface = false,
        Routes = Array.Empty<GuideReachabilityRoute>(),
    };

    /// <summary>
    /// Reads the G645 declaration from packet YAML.  The canonical shape is:
    ///
    /// <code>
    /// guide_reachability:
    ///   no_role_facing_surface: false
    ///   routes:
    ///     - guide_surface: "guide workflow task implementation-loop"
    ///       role: implementation
    ///       target_surface: "the new command"
    /// </code>
    ///
    /// A small set of equivalent field spellings is accepted for packets
    /// authored during the preview lane.  Missing required route fields and a
    /// present-but-unreadable declaration fail closed rather than becoming a
    /// false all-clear.
    /// </summary>
    public static GuideReachabilityDeclaration Read(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new InvalidOperationException("Packet YAML is empty, so its guide-reachability declaration cannot be read.");
        }

        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            root = stream.Documents.Count == 0
                ? throw new InvalidOperationException("Packet YAML is empty or is not a mapping.")
                : stream.Documents[0].RootNode as YamlMappingNode
                    ?? throw new InvalidOperationException("Packet YAML is empty or is not a mapping.");
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new InvalidOperationException($"Packet YAML could not be parsed: {exception.Message}");
        }

        if (!root.Children.TryGetValue(new YamlScalarNode("guide_reachability"), out var declarationNode))
        {
            return Absent;
        }

        if (declarationNode is YamlSequenceNode sequence)
        {
            // A sequence is a convenient preview spelling for routes, but an
            // empty sequence is a blank answer, not an explicit no-surface
            // decision.
            if (sequence.Children.Count == 0)
            {
                throw new InvalidOperationException(
                    "Packet field 'guide_reachability' is an empty list. Declare no_role_facing_surface: true "
                    + "or name at least one guide route.");
            }

            return new GuideReachabilityDeclaration
            {
                IsDeclared = true,
                NoRoleFacingSurface = false,
                Routes = ReadRoutes(sequence.Children),
            };
        }

        if (declarationNode is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException(
                "Packet field 'guide_reachability' must be a mapping or a non-empty sequence.");
        }

        var explicitNoSurface = ReadOptionalBoolean(mapping, "no_role_facing_surface")
            ?? ReadOptionalBoolean(mapping, "no_role_facing")
            ?? ReadOptionalBoolean(mapping, "no_surface")
            ?? ReadOptionalBoolean(mapping, "none")
            ?? ReadOptionalBoolean(mapping, "not_applicable")
            ?? ReadOptionalBoolean(mapping, "declared_no_surface");
        if (explicitNoSurface is null
            && FirstChild(mapping, "status", "decision") is YamlScalarNode statusNode
            && statusNode.Value is { } status
            && (status.Trim().Equals("no-role-facing-surface", StringComparison.OrdinalIgnoreCase)
                || status.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                || status.Trim().Equals("not-applicable", StringComparison.OrdinalIgnoreCase)))
        {
            explicitNoSurface = true;
        }

        var routeNode = FirstChild(mapping, "routes", "guide_routes", "entries", "declarations", "surfaces");
        var routes = routeNode switch
        {
            null => HasRouteScalar(mapping) ? ReadRoutes([mapping]) : Array.Empty<GuideReachabilityRoute>(),
            YamlSequenceNode routeSequence => ReadRoutes(routeSequence.Children),
            _ => throw new InvalidOperationException(
                "Packet field 'guide_reachability.routes' must be a sequence of guide routes."),
        };

        // `required: false` is accepted as an explicit no-surface answer for
        // preview packets that mirror the knowledge_updates shape.  It is not
        // used as the canonical generated spelling because a named
        // no_role_facing_surface field is clearer to a human author.
        var required = ReadOptionalBoolean(mapping, "required");
        if (explicitNoSurface is null && required is false && routes.Count == 0)
        {
            explicitNoSurface = true;
        }

        if (explicitNoSurface is true)
        {
            if (routes.Count > 0)
            {
                throw new InvalidOperationException(
                    "Packet guide_reachability cannot declare no_role_facing_surface: true and guide routes together.");
            }

            return new GuideReachabilityDeclaration
            {
                IsDeclared = true,
                NoRoleFacingSurface = true,
                Routes = Array.Empty<GuideReachabilityRoute>(),
            };
        }

        if (routes.Count == 0)
        {
            throw new InvalidOperationException(
                "Packet guide_reachability is present but blank. Name guide_surface, role, and target_surface "
                + "for each route, or explicitly set no_role_facing_surface: true.");
        }

        return new GuideReachabilityDeclaration
        {
            IsDeclared = true,
            NoRoleFacingSurface = false,
            Routes = routes,
        };
    }

    private static IReadOnlyList<GuideReachabilityRoute> ReadRoutes(IEnumerable<YamlNode> nodes)
    {
        var routes = new List<GuideReachabilityRoute>();
        foreach (var node in nodes)
        {
            if (node is not YamlMappingNode mapping)
            {
                throw new InvalidOperationException(
                    "Each guide_reachability route must be a mapping with guide_surface, role, and target_surface.");
            }

            var guide = ReadRequiredScalar(mapping, "guide_surface", "guide", "guide_path", "guide_name");
            var role = ReadRequiredScalar(mapping, "role", "routing_role");
            var target = ReadRequiredScalar(mapping, "target_surface", "surface", "target", "added_surface");
            routes.Add(new GuideReachabilityRoute
            {
                GuideSurface = guide,
                Role = role,
                TargetSurface = target,
            });
        }

        return routes
            .GroupBy(route => (route.GuideSurface, route.Role, route.TargetSurface))
            .Select(group => group.First())
            .ToArray();
    }

    private static bool HasRouteScalar(YamlMappingNode mapping) =>
        FirstChild(mapping, "guide_surface", "guide", "guide_path", "guide_name") is not null
        || FirstChild(mapping, "role", "routing_role") is not null
        || FirstChild(mapping, "target_surface", "surface", "target", "added_surface") is not null;

    private static YamlNode? FirstChild(YamlMappingNode mapping, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
            {
                return node;
            }
        }

        return null;
    }

    private static string ReadRequiredScalar(YamlMappingNode mapping, params string[] keys)
    {
        var node = FirstChild(mapping, keys);
        var value = node is YamlScalarNode scalar ? scalar.Value?.Trim() : null;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Packet guide_reachability route is missing a non-blank {keys[0]} value.");
        }

        return value;
    }

    private static bool? ReadOptionalBoolean(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return null;
        }

        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw new InvalidOperationException($"Packet field 'guide_reachability.{key}' must be a boolean.");
        }

        if (!bool.TryParse(scalar.Value.Trim(), out var value))
        {
            throw new InvalidOperationException(
                $"Packet field 'guide_reachability.{key}' is '{scalar.Value}', which is not a boolean.");
        }

        return value;
    }
}
