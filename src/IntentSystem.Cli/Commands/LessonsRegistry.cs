using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>Strict, read-only selection from one explicitly pinned local lessons registry.</summary>
internal static class LessonsRegistry
{
    internal const int MaximumBytes = 1024 * 1024;
    internal const int MaximumLessons = 128;
    internal const string RootRelativePath = ".intent-cli/lessons";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    internal sealed record AppliesTo(
        [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
        [property: JsonPropertyName("target_repo")] string TargetRepo,
        [property: JsonPropertyName("domain")] string Domain,
        [property: JsonPropertyName("team")] string Team,
        [property: JsonPropertyName("execution_units")] IReadOnlyList<string> ExecutionUnits);

    internal sealed record Lifecycle(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("active_from")] string ActiveFrom,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        [property: JsonPropertyName("expires_at")] string? ExpiresAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        [property: JsonPropertyName("superseded_by")] string? SupersededBy);

    internal sealed record Entry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("applies_to")] AppliesTo AppliesTo,
        [property: JsonPropertyName("rule")] string Rule,
        [property: JsonPropertyName("enforced_by")] IReadOnlyList<string> EnforcedBy,
        [property: JsonPropertyName("lifecycle")] Lifecycle Lifecycle,
        [property: JsonPropertyName("enforcement_state")] string EnforcementState);

    internal sealed record Selection(
        [property: JsonPropertyName("registry_id")] string RegistryId,
        [property: JsonPropertyName("registry_path")] string RegistryPath,
        [property: JsonPropertyName("registry_sha256")] string RegistrySha256,
        [property: JsonPropertyName("selected_ids")] IReadOnlyList<string> SelectedIds,
        [property: JsonPropertyName("entries")] IReadOnlyList<Entry> Entries,
        [property: JsonIgnore] byte[] RawBytes);

    private sealed record AuthoredLesson(
        string Id,
        string Source,
        AppliesTo AppliesTo,
        string Rule,
        IReadOnlyList<string> EnforcedBy,
        Lifecycle Lifecycle,
        DateTimeOffset ActiveFrom,
        DateTimeOffset? ExpiresAt);

    private enum ValidationFailure { Invalid, ResourceLimit }

    internal static bool IsRegistryId(string? value) => IsLessonId(value);

    internal static bool TrySelect(
        byte[] rawBytes,
        string registryPath,
        string registryId,
        string expectedSha256,
        IReadOnlyList<string> selectedIds,
        string role,
        string targetRepo,
        string domain,
        string team,
        string executionUnit,
        DateTimeOffset asOf,
        out Selection? selection,
        out string cause,
        out string detail)
    {
        selection = null;
        cause = "brief-lessons-invalid";
        detail = "the lessons registry is invalid";

        if (rawBytes.Length > MaximumBytes)
        {
            cause = "brief-resource-limit";
            detail = "the lessons registry exceeds the 1 MiB byte limit";
            return false;
        }

        if (rawBytes.Length == 0)
        {
            cause = "brief-lessons-unavailable";
            detail = "the lessons registry is empty or unavailable";
            return false;
        }

        var actualSha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(actualSha256, expectedSha256))
        {
            cause = "brief-lessons-digest-mismatch";
            detail = "the lessons registry bytes do not match --lessons-sha256";
            return false;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(rawBytes);
        }
        catch (DecoderFallbackException)
        {
            detail = "the lessons registry is not strict UTF-8";
            return false;
        }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        if (string.IsNullOrWhiteSpace(text))
        {
            cause = "brief-lessons-unavailable";
            detail = "the lessons registry contains no JSON text";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, JsonOptions);
        }
        catch (JsonException)
        {
            // A parser-depth exception and malformed JSON are both safely refused.
            // The explicit tree check below gives ordinary depth overflow its own cause.
            cause = "brief-lessons-invalid";
            detail = "the lessons registry is not one valid JSON document";
            return false;
        }
        using (document)
        {
            if (ExceedsDepth(document.RootElement, 1))
            {
                cause = "brief-resource-limit";
                detail = "the lessons registry exceeds JSON depth 8";
                return false;
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !HasProperties(document.RootElement,
                    ["schema_version", "registry_id", "lessons"], out _))
            {
                detail = "the lessons registry root must contain only schema_version, registry_id and lessons";
                return false;
            }

            var root = document.RootElement;
            var version = root.GetProperty("schema_version");
            if (version.ValueKind != JsonValueKind.Number || version.GetRawText() != "1")
            {
                detail = "schema_version must be the JSON integer 1";
                return false;
            }

            if (!TryString(root.GetProperty("registry_id"), out var authoredRegistryId)
                || !IsRegistryId(authoredRegistryId)
                || !StringComparer.Ordinal.Equals(authoredRegistryId, registryId))
            {
                detail = "registry_id must be a valid ID exactly matching --lessons-registry";
                return false;
            }

            var rowsNode = root.GetProperty("lessons");
            if (rowsNode.ValueKind != JsonValueKind.Array)
            {
                detail = "lessons must be an array";
                return false;
            }
            if (rowsNode.GetArrayLength() > MaximumLessons)
            {
                cause = "brief-resource-limit";
                detail = "lessons exceeds the 128-row limit";
                return false;
            }

            var lessons = new List<AuthoredLesson>(rowsNode.GetArrayLength());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var foldedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rowIndex = 0;
            foreach (var row in rowsNode.EnumerateArray())
            {
                if (!TryLesson(row, rowIndex, out var lesson, out var rowFailure, out var rowFailureKind))
                {
                    if (rowFailureKind == ValidationFailure.ResourceLimit) cause = "brief-resource-limit";
                    detail = rowFailure;
                    return false;
                }
                if (!ids.Add(lesson!.Id) || !foldedIds.Add(lesson.Id))
                {
                    detail = $"lessons[{rowIndex}].id is duplicated or a case alias";
                    return false;
                }
                lessons.Add(lesson);
                rowIndex++;
            }

            var byId = lessons.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var lesson in lessons)
            {
                if (lesson.Lifecycle.State == "active")
                {
                    if (lesson.Lifecycle.SupersededBy is not null)
                    {
                        detail = $"lesson '{lesson.Id}' active lifecycle requires superseded_by=null";
                        return false;
                    }
                    continue;
                }

                var successorId = lesson.Lifecycle.SupersededBy;
                if (successorId is null || StringComparer.Ordinal.Equals(successorId, lesson.Id)
                    || !byId.TryGetValue(successorId, out var successor)
                    || successor.Lifecycle.State != "active")
                {
                    detail = $"lesson '{lesson.Id}' has an invalid superseded_by reference";
                    return false;
                }
            }

            if (selectedIds.Count is < 1 or > 16)
            {
                detail = "one to sixteen --lesson IDs are required";
                return false;
            }
            var orderedIds = selectedIds.Order(StringComparer.Ordinal).ToArray();
            var selected = new List<Entry>(orderedIds.Length);
            foreach (var selectedId in orderedIds)
            {
                if (!byId.TryGetValue(selectedId, out var lesson))
                {
                    cause = "brief-lessons-missing";
                    detail = $"selected lesson '{selectedId}' is not present in the registry";
                    return false;
                }

                if (!lesson.AppliesTo.Roles.Contains(role, StringComparer.Ordinal)
                    || !StringComparer.Ordinal.Equals(lesson.AppliesTo.TargetRepo, targetRepo)
                    || !StringComparer.Ordinal.Equals(lesson.AppliesTo.Domain, domain)
                    || !StringComparer.Ordinal.Equals(lesson.AppliesTo.Team, team)
                    || !lesson.AppliesTo.ExecutionUnits.Contains(executionUnit, StringComparer.Ordinal))
                {
                    cause = "brief-lessons-inapplicable";
                    detail = $"selected lesson '{lesson.Id}' does not apply to the explicit brief identity";
                    return false;
                }

                if (lesson.Lifecycle.State == "superseded")
                {
                    cause = "brief-lessons-superseded";
                    detail = $"selected lesson '{lesson.Id}' is superseded";
                    return false;
                }
                if (lesson.ActiveFrom > asOf)
                {
                    cause = "brief-lessons-not-yet-active";
                    detail = $"selected lesson '{lesson.Id}' is not active at --as-of";
                    return false;
                }
                if (lesson.ExpiresAt is not null && asOf >= lesson.ExpiresAt.Value)
                {
                    cause = "brief-lessons-expired";
                    detail = $"selected lesson '{lesson.Id}' is expired at --as-of";
                    return false;
                }

                selected.Add(new Entry(lesson.Id, lesson.Source, lesson.AppliesTo, lesson.Rule,
                    lesson.EnforcedBy, lesson.Lifecycle,
                    lesson.EnforcedBy.Count == 0 ? "advisory" : "declared-not-verified"));
            }

            selection = new Selection(registryId, registryPath, actualSha256, orderedIds, selected, rawBytes);
            cause = "";
            detail = "";
            return true;
        }
    }

    private static bool TryLesson(JsonElement node, int index, out AuthoredLesson? lesson, out string detail,
        out ValidationFailure failureKind)
    {
        lesson = null;
        failureKind = ValidationFailure.Invalid;
        var prefix = $"lessons[{index}]";
        if (node.ValueKind != JsonValueKind.Object)
        {
            detail = $"{prefix} must be an object with the exact required fields";
            return false;
        }
        if (!HasProperties(node, ["id", "source", "applies_to", "rule", "enforced_by", "lifecycle"], out var shape))
        {
            detail = $"{prefix} {shape}";
            return false;
        }

        var idNode = node.GetProperty("id");
        var sourceNode = node.GetProperty("source");
        var ruleNode = node.GetProperty("rule");
        if (!TryString(idNode, out var id) || !IsLessonId(id))
        {
            detail = $"{prefix}.id is not a valid lesson ID";
            return false;
        }
        if (!TryBoundedText(sourceNode, 4096, out var source, out failureKind))
        {
            detail = $"{prefix}.source must be valid non-whitespace text of at most 4096 UTF-16 units";
            return false;
        }
        if (!TryBoundedText(ruleNode, 4096, out var rule, out failureKind))
        {
            detail = $"{prefix}.rule must be valid non-whitespace text of at most 4096 UTF-16 units";
            return false;
        }

        if (!TryAppliesTo(node.GetProperty("applies_to"), prefix, out var appliesTo, out detail, out failureKind)) return false;
        if (!TryStringArray(node.GetProperty("enforced_by"), minimum: 0, maximum: 8,
                maximumStringLength: 256, unique: StringComparer.Ordinal, out var enforcedBy, out failureKind))
        {
            detail = $"{prefix}.enforced_by must contain at most eight unique non-whitespace strings of at most 256 UTF-16 units";
            return false;
        }
        if (!TryLifecycle(node.GetProperty("lifecycle"), prefix, out var lifecycle, out var activeFrom,
                out var expiresAt, out detail)) return false;

        lesson = new(id, source, appliesTo!, rule, enforcedBy!, lifecycle!, activeFrom, expiresAt);
        detail = "";
        return true;
    }

    private static bool TryAppliesTo(JsonElement node, string prefix, out AppliesTo? appliesTo, out string detail,
        out ValidationFailure failureKind)
    {
        appliesTo = null;
        failureKind = ValidationFailure.Invalid;
        if (node.ValueKind != JsonValueKind.Object)
        {
            detail = $"{prefix}.applies_to must be an object with the exact required fields";
            return false;
        }
        if (!HasProperties(node, ["roles", "target_repo", "domain", "team", "execution_units"], out var shape))
        {
            detail = $"{prefix}.applies_to {shape}";
            return false;
        }

        var rolesNode = node.GetProperty("roles");
        if (!TryStringArray(rolesNode, 1, 2, int.MaxValue, StringComparer.Ordinal, out var roles, out failureKind)
            || roles!.Any(role => role is not ("builder" or "reviewer")))
        {
            detail = $"{prefix}.applies_to.roles must contain one or two unique builder/reviewer roles";
            return false;
        }
        if (!TryString(node.GetProperty("target_repo"), out var targetRepo)
            || !CrossRuntimeReviewPaths.IsRepositoryName(targetRepo))
        {
            detail = $"{prefix}.applies_to.target_repo is invalid";
            return false;
        }
        if (!TryString(node.GetProperty("domain"), out var domain)
            || !RulingArtifact.TryIdentifier(domain, out _))
        {
            detail = $"{prefix}.applies_to.domain is invalid";
            return false;
        }
        if (!TryString(node.GetProperty("team"), out var team)
            || !RulingArtifact.TryIdentifier(team, out _))
        {
            detail = $"{prefix}.applies_to.team is invalid";
            return false;
        }

        if (!TryStringArray(node.GetProperty("execution_units"), 1, 16, 128,
                StringComparer.OrdinalIgnoreCase, out var executionUnits, out failureKind)
            || executionUnits!.Any(unit => !RulingArtifact.TryIdentifier(unit, out _)))
        {
            detail = $"{prefix}.applies_to.execution_units must contain one to sixteen unique canonical unit identifiers";
            return false;
        }

        appliesTo = new(roles!, targetRepo, domain, team, executionUnits!);
        detail = "";
        return true;
    }

    private static bool TryLifecycle(JsonElement node, string prefix, out Lifecycle? lifecycle,
        out DateTimeOffset activeFrom, out DateTimeOffset? expiresAt, out string detail)
    {
        lifecycle = null;
        activeFrom = default;
        expiresAt = null;
        if (node.ValueKind != JsonValueKind.Object)
        {
            detail = $"{prefix}.lifecycle must be an object with the exact required fields";
            return false;
        }
        if (!HasProperties(node, ["state", "active_from", "expires_at", "superseded_by"], out var shape))
        {
            detail = $"{prefix}.lifecycle {shape}";
            return false;
        }

        if (!TryString(node.GetProperty("state"), out var state) || state is not ("active" or "superseded"))
        {
            detail = $"{prefix}.lifecycle.state must be active or superseded";
            return false;
        }
        if (!TryString(node.GetProperty("active_from"), out var activeFromText)
            || !TryCanonicalTimestamp(activeFromText, out activeFrom))
        {
            detail = $"{prefix}.lifecycle.active_from must be canonical whole-second UTC";
            return false;
        }

        var expiresNode = node.GetProperty("expires_at");
        string? expiresText;
        if (expiresNode.ValueKind == JsonValueKind.Null) expiresText = null;
        else if (!TryString(expiresNode, out expiresText) || !TryCanonicalTimestamp(expiresText!, out var expires))
        {
            detail = $"{prefix}.lifecycle.expires_at must be null or canonical whole-second UTC";
            return false;
        }
        else expiresAt = expires;

        if (expiresAt is not null && expiresAt.Value <= activeFrom)
        {
            detail = $"{prefix}.lifecycle.expires_at must be later than active_from";
            return false;
        }

        var supersededNode = node.GetProperty("superseded_by");
        string? supersededBy;
        if (supersededNode.ValueKind == JsonValueKind.Null) supersededBy = null;
        else if (!TryString(supersededNode, out supersededBy) || !IsLessonId(supersededBy))
        {
            detail = $"{prefix}.lifecycle.superseded_by must be null or an exact lesson ID";
            return false;
        }
        if (state == "active" && supersededBy is not null)
        {
            detail = $"{prefix}.lifecycle active state requires superseded_by=null";
            return false;
        }
        if (state == "superseded" && supersededBy is null)
        {
            detail = $"{prefix}.lifecycle superseded state requires superseded_by";
            return false;
        }

        lifecycle = new(state, activeFromText, expiresText, supersededBy);
        detail = "";
        return true;
    }

    private static bool HasProperties(JsonElement node, IReadOnlyCollection<string> exactNames, out string? detail)
    {
        detail = null;
        var allowed = new HashSet<string>(exactNames, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            string propertyName;
            try { propertyName = property.Name; }
            catch (InvalidOperationException)
            {
                detail = "contains a property name with invalid Unicode";
                return false;
            }
            if (!allowed.Contains(propertyName))
            {
                detail = "contains an unknown property";
                return false;
            }
            if (!seen.Add(propertyName))
            {
                detail = "contains a duplicate property";
                return false;
            }
        }
        if (seen.Count != allowed.Count)
        {
            detail = "is missing a required property";
            return false;
        }
        return true;
    }

    private static bool TryStringArray(JsonElement node, int minimum, int maximum, int maximumStringLength,
        StringComparer unique, out IReadOnlyList<string>? values, out ValidationFailure failureKind)
    {
        values = null;
        failureKind = ValidationFailure.Invalid;
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() < minimum)
            return false;
        if (node.GetArrayLength() > maximum)
        {
            failureKind = ValidationFailure.ResourceLimit;
            return false;
        }
        var list = new List<string>(node.GetArrayLength());
        var seen = new HashSet<string>(unique);
        foreach (var item in node.EnumerateArray())
        {
            if (!TryString(item, out var value) || string.IsNullOrWhiteSpace(value)
                || !seen.Add(value))
                return false;
            if (value.Length > maximumStringLength)
            {
                failureKind = ValidationFailure.ResourceLimit;
                return false;
            }
            list.Add(value);
        }
        values = list;
        return true;
    }

    private static bool TryBoundedText(JsonElement node, int maximumLength, out string value,
        out ValidationFailure failureKind)
    {
        value = "";
        failureKind = ValidationFailure.Invalid;
        if (!TryString(node, out var candidate) || string.IsNullOrWhiteSpace(candidate)
            || !IsWellFormedUtf16(candidate))
            return false;
        if (candidate.Length > maximumLength)
        {
            failureKind = ValidationFailure.ResourceLimit;
            return false;
        }
        value = candidate;
        return true;
    }

    private static bool TryString(JsonElement node, out string value)
    {
        value = "";
        if (node.ValueKind != JsonValueKind.String) return false;
        string? candidate;
        try { candidate = node.GetString(); }
        catch (InvalidOperationException)
        {
            // System.Text.Json preserves an escaped lone surrogate in the syntax tree,
            // then throws when materializing it as UTF-16 text. Treat that malformed
            // Unicode scalar as ordinary schema invalidity, not as a command exception.
            return false;
        }
        if (candidate is null || !IsWellFormedUtf16(candidate)) return false;
        value = candidate;
        return true;
    }

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(character)) return false;
        }
        return true;
    }

    internal static bool IsLessonId(string? value)
    {
        if (value is null || value.Length is < 1 or > 64 || !char.IsAsciiLetter(value[0])) return false;
        for (var index = 1; index < value.Length; index++)
            if (!char.IsAsciiLetterOrDigit(value[index]) && value[index] is not ('.' or '_' or '-')) return false;
        return true;
    }

    private static bool TryCanonicalTimestamp(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value)
        && value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) == text;

    private static bool ExceedsDepth(JsonElement element, int containerDepth)
    {
        if (containerDepth > 8) return true;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                if (IsContainer(property.Value) && ExceedsDepth(property.Value, containerDepth + 1)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (IsContainer(item) && ExceedsDepth(item, containerDepth + 1)) return true;
        }
        return false;
    }

    private static bool IsContainer(JsonElement element) => element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
}
