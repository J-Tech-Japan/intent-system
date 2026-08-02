using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G580: discovers shared assignments to settable CLI statics directly from
/// the product assembly and this test assembly's IL. A new seam or assigning
/// test class therefore enters the guard without updating a registry.
/// </summary>
public sealed class SharedStaticSeamSerializationMetaTests
{
    [Fact]
    public void EveryCliStaticAssignedByMultipleTestClasses_IsProvablySerialized_G580()
    {
        var analysis = StaticSeamAnalysis.Discover();
        Console.WriteLine(
            $"Discovered {analysis.SettableSeamCount} settable CLI statics, "
            + $"{analysis.AssigningClassCount} assigning test classes, and "
            + $"{analysis.SharedAssignments.Count} multi-class assignments.");

        var offenders = analysis.SharedAssignments
            .Where(assignment => !analysis.AreSerializedTogether(assignment.AssigningClasses))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "settable CLI statics assigned by multiple test classes must be explicitly serialized. "
            + "Put every assigning class in one xUnit collection, or prove every distinct collection has "
            + "CollectionDefinition(DisableParallelization = true). Offenders:\n"
            + string.Join("\n", offenders.Select(analysis.Describe)));
    }

    [Fact]
    public void FiveSplitCollectionCases_AreExplicitlyProtectedByXunitDisableParallelization_G580()
    {
        var analysis = StaticSeamAnalysis.Discover();
        var splitCases = analysis.DiscoverSplitCollectionCases();
        var expectedSplitCases = new[]
        {
            new SplitCollectionCase(
                "IntentSystem.Cli.Commands.AutomationIssueBlockCommand.UtcNowFactory",
                typeof(AutomationHostReviewPreflightCommandTests),
                typeof(AutomationStalledWorkCommandTests)),
            new SplitCollectionCase(
                "IntentSystem.Cli.Commands.AutomationIssueBlockCommand.UtcNowFactory",
                typeof(AutomationIssueBlockCommandTests),
                typeof(AutomationStalledWorkCommandTests)),
            new SplitCollectionCase(
                "IntentSystem.Cli.Commands.ClarifyOpenCommand.TimestampFactory",
                typeof(AutomationStalledWorkCommandTests),
                typeof(ClarifyOpenCommandTests)),
            new SplitCollectionCase(
                "IntentSystem.Cli.Commands.ClarifyOpenCommand.TimestampFactory",
                typeof(AutomationStalledWorkCommandTests),
                typeof(ClarifyOpenScaffoldedPacketTests)),
            new SplitCollectionCase(
                "IntentSystem.Cli.Commands.ClarifyOpenCommand.TimestampFactory",
                typeof(AutomationStalledWorkCommandTests),
                typeof(CommandRouterTests)),
        };

        // xUnit 2.9.3 documents CollectionDefinitionAttribute.DisableParallelization
        // as determining whether a collection runs in parallel with ANY other
        // collection. This explicit record of the five existing cross-collection
        // class pairs is safe only while every collection involved keeps that
        // setting. Discovery remains independent of this accepted-case inventory.
        Assert.True(
            splitCases.SequenceEqual(expectedSplitCases)
            && splitCases.All(split => analysis.AreSerializedTogether([split.LeftClass, split.RightClass])),
            "expected the five recorded shared-static assigning-class pairs split across distinct explicitly "
            + "non-parallel xUnit collections, with no additions or substitutions. Every involved CollectionDefinition must keep "
            + $"DisableParallelization = true. Discovered {splitCases.Count}:\n"
            + string.Join("\n", splitCases.Select(analysis.Describe)));

        foreach (var split in splitCases)
        {
            Console.WriteLine(analysis.Describe(split));
        }
    }

    private sealed class StaticSeamAnalysis
    {
        private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue =
            typeof(OpCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(OpCode))
                .Select(field => (OpCode)field.GetValue(null)!)
                .ToDictionary(opCode => unchecked((ushort)opCode.Value));

        private readonly IReadOnlyDictionary<Type, string?> collectionByClass;
        private readonly IReadOnlyDictionary<string, bool> collectionParallelizationDisabled;
        private readonly bool assemblyParallelizationDisabled;

        private StaticSeamAnalysis(
            int settableSeamCount,
            int assigningClassCount,
            IReadOnlyList<StaticSeamAssignment> sharedAssignments,
            IReadOnlyDictionary<Type, string?> collectionByClass,
            IReadOnlyDictionary<string, bool> collectionParallelizationDisabled,
            bool assemblyParallelizationDisabled)
        {
            SettableSeamCount = settableSeamCount;
            AssigningClassCount = assigningClassCount;
            SharedAssignments = sharedAssignments;
            this.collectionByClass = collectionByClass;
            this.collectionParallelizationDisabled = collectionParallelizationDisabled;
            this.assemblyParallelizationDisabled = assemblyParallelizationDisabled;
        }

        public IReadOnlyList<StaticSeamAssignment> SharedAssignments { get; }

        public int SettableSeamCount { get; }

        public int AssigningClassCount { get; }

        public static StaticSeamAnalysis Discover()
        {
            var cliAssembly = typeof(IntentSystem.Cli.Commands.AutomationInstalledCliSurfaceProbe).Assembly;
            var testAssembly = typeof(SharedStaticSeamSerializationMetaTests).Assembly;
            var seamNames = DiscoverSettableStaticSeams(cliAssembly);
            var assigningClasses = seamNames.Keys.ToDictionary(
                key => key,
                _ => new HashSet<Type>());

            foreach (var type in testAssembly.GetTypes())
            {
                foreach (var method in DeclaredMethodsAndConstructors(type))
                {
                    foreach (var assignedMember in ReadAssignedStaticMembers(method))
                    {
                        if (assigningClasses.TryGetValue(assignedMember, out var classes))
                        {
                            classes.Add(OutermostDeclaringType(type));
                        }
                    }
                }
            }

            var sharedAssignments = assigningClasses
                .Where(entry => entry.Value.Count >= 2)
                .Select(entry => new StaticSeamAssignment(
                    seamNames[entry.Key],
                    entry.Value.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray()))
                .OrderBy(assignment => assignment.SeamName, StringComparer.Ordinal)
                .ToArray();

            var collectionByClass = testAssembly.GetTypes()
                .Where(type => !type.IsNested)
                .ToDictionary(type => type, ReadCollectionName);

            var collectionDefinitions = ReadCollectionDefinitions(testAssembly);
            var assemblyParallelizationDisabled = ReadAssemblyParallelizationDisabled(testAssembly);

            return new StaticSeamAnalysis(
                seamNames.Count,
                assigningClasses.Values.SelectMany(classes => classes).Distinct().Count(),
                sharedAssignments,
                collectionByClass,
                collectionDefinitions,
                assemblyParallelizationDisabled);
        }

        public bool AreSerializedTogether(IReadOnlyList<Type> assigningClasses)
        {
            if (assemblyParallelizationDisabled)
            {
                return true;
            }

            var collections = assigningClasses
                .Select(GetCollectionName)
                .ToArray();

            if (collections.Any(name => name is null))
            {
                return false;
            }

            var distinctCollections = collections
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (distinctCollections.Length == 1)
            {
                return true;
            }

            return distinctCollections.All(name =>
                collectionParallelizationDisabled.TryGetValue(name, out var disabled) && disabled);
        }

        public IReadOnlyList<SplitCollectionCase> DiscoverSplitCollectionCases()
        {
            var cases = new List<SplitCollectionCase>();
            foreach (var assignment in SharedAssignments)
            {
                for (var leftIndex = 0; leftIndex < assignment.AssigningClasses.Count; leftIndex++)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < assignment.AssigningClasses.Count; rightIndex++)
                    {
                        var left = assignment.AssigningClasses[leftIndex];
                        var right = assignment.AssigningClasses[rightIndex];
                        var leftCollection = GetCollectionName(left);
                        var rightCollection = GetCollectionName(right);

                        if (leftCollection is not null
                            && rightCollection is not null
                            && !string.Equals(leftCollection, rightCollection, StringComparison.Ordinal))
                        {
                            cases.Add(new SplitCollectionCase(assignment.SeamName, left, right));
                        }
                    }
                }
            }

            return cases;
        }

        public string Describe(StaticSeamAssignment assignment)
        {
            var classes = assignment.AssigningClasses.Select(type =>
            {
                var collection = GetCollectionName(type);
                var protection = collection is not null
                    && collectionParallelizationDisabled.TryGetValue(collection, out var disabled)
                    && disabled
                        ? "DisableParallelization=true"
                        : "parallelization-not-disabled";
                return $"{type.FullName} [collection={collection ?? "<implicit-per-class>"}; {protection}]";
            });

            return $"{assignment.SeamName}: {string.Join(", ", classes)}";
        }

        public string Describe(SplitCollectionCase split) =>
            $"{split.SeamName}: {DescribeClass(split.LeftClass)} <> {DescribeClass(split.RightClass)}";

        private string DescribeClass(Type type)
        {
            var collection = GetCollectionName(type);
            var protection = collection is not null
                && collectionParallelizationDisabled.TryGetValue(collection, out var disabled)
                && disabled
                    ? "DisableParallelization=true"
                    : "parallelization-not-disabled";
            return $"{type.FullName} [collection={collection ?? "<implicit-per-class>"}; {protection}]";
        }

        private string? GetCollectionName(Type type) =>
            collectionByClass.TryGetValue(type, out var collection) ? collection : ReadCollectionName(type);

        private static IReadOnlyDictionary<MemberKey, string> DiscoverSettableStaticSeams(Assembly cliAssembly)
        {
            var seams = new Dictionary<MemberKey, string>();

            foreach (var type in cliAssembly.GetTypes())
            {
                foreach (var property in type.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var setter = property.GetSetMethod(nonPublic: true);
                    if (setter is null || !setter.IsStatic || !IsTestAccessible(setter))
                    {
                        continue;
                    }

                    seams[MemberKey.From(setter)] = $"{type.FullName}.{property.Name}";
                }

            }

            return seams;
        }

        private static bool IsTestAccessible(MethodBase method) =>
            method.IsPublic || method.IsAssembly || method.IsFamilyOrAssembly;

        private static IEnumerable<MethodBase> DeclaredMethodsAndConstructors(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            return type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
        }

        private static IEnumerable<MemberKey> ReadAssignedStaticMembers(MethodBase method)
        {
            var body = method.GetMethodBody();
            var il = body?.GetILAsByteArray();
            if (il is null)
            {
                yield break;
            }

            var declaringTypeArguments = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
            var methodArguments = method is MethodInfo methodInfo
                ? methodInfo.GetGenericArguments()
                : Type.EmptyTypes;

            for (var offset = 0; offset < il.Length;)
            {
                var opCode = ReadOpCode(il, ref offset);
                var operandOffset = offset;

                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    var token = BitConverter.ToInt32(il, operandOffset);
                    if (method.Module.ResolveMethod(token, declaringTypeArguments, methodArguments) is MethodInfo called
                        && called.IsStatic
                        && called.IsSpecialName
                        && called.Name.StartsWith("set_", StringComparison.Ordinal))
                    {
                        yield return MemberKey.From(called);
                    }
                }
                offset += OperandSize(opCode.OperandType, il, operandOffset);
            }
        }

        private static OpCode ReadOpCode(byte[] il, ref int offset)
        {
            ushort value = il[offset++];
            if (value == 0xfe)
            {
                value = (ushort)(0xfe00 | il[offset++]);
            }

            return OpCodesByValue.TryGetValue(value, out var opCode)
                ? opCode
                : throw new InvalidOperationException($"unknown IL opcode 0x{value:x4}.");
        }

        private static int OperandSize(OperandType operandType, byte[] il, int operandOffset) =>
            operandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
                    or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
                    or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, operandOffset) * 4),
                _ => throw new InvalidOperationException($"unsupported IL operand type {operandType}."),
            };

        private static Type OutermostDeclaringType(Type type)
        {
            while (type.DeclaringType is not null)
            {
                type = type.DeclaringType;
            }

            return type;
        }

        private static string? ReadCollectionName(Type type)
        {
            var attribute = type.CustomAttributes.SingleOrDefault(data =>
                data.AttributeType == typeof(CollectionAttribute));
            return attribute?.ConstructorArguments.SingleOrDefault().Value as string;
        }

        private static IReadOnlyDictionary<string, bool> ReadCollectionDefinitions(Assembly assembly) =>
            assembly.GetTypes()
                .Select(type => type.CustomAttributes.SingleOrDefault(data =>
                    data.AttributeType == typeof(CollectionDefinitionAttribute)))
                .Where(attribute => attribute is not null)
                .ToDictionary(
                    attribute => (string)attribute!.ConstructorArguments.Single().Value!,
                    attribute => ReadBooleanNamedArgument(attribute!, "DisableParallelization"),
                    StringComparer.Ordinal);

        private static bool ReadAssemblyParallelizationDisabled(Assembly assembly)
        {
            var attribute = assembly.CustomAttributes.SingleOrDefault(data =>
                data.AttributeType == typeof(CollectionBehaviorAttribute));
            return attribute is not null
                && ReadBooleanNamedArgument(attribute, "DisableTestParallelization");
        }

        private static bool ReadBooleanNamedArgument(CustomAttributeData attribute, string memberName) =>
            attribute.NamedArguments.SingleOrDefault(argument =>
                string.Equals(argument.MemberName, memberName, StringComparison.Ordinal)).TypedValue.Value as bool? ?? false;
    }

    private sealed record StaticSeamAssignment(string SeamName, IReadOnlyList<Type> AssigningClasses);

    private sealed record SplitCollectionCase(string SeamName, Type LeftClass, Type RightClass);

    private readonly record struct MemberKey(Guid ModuleVersionId, int MetadataToken)
    {
        public static MemberKey From(MemberInfo member) =>
            new(member.Module.ModuleVersionId, member.MetadataToken);
    }
}
