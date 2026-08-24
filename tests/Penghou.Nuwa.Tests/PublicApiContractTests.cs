using FluentAssertions;
using System.Reflection;
using System.Text;

namespace Penghou.Nuwa.Tests;

public sealed class PublicApiContractTests
{
    [Fact]
    public void PublicSurface_MatchesLockedSnapshot()
    {
        GeneratePublicApiSnapshot()
            .TrimEnd('\n')
            .Should()
            .Be(ExpectedPublicApi);
    }

    private const string ExpectedPublicApi =
        """
        type Penghou.Nuwa.Extensions.ServiceCollectionExtensions : static class
          method static Microsoft.Extensions.DependencyInjection.IServiceCollection AddJsonRepair(Microsoft.Extensions.DependencyInjection.IServiceCollection)
          method static Microsoft.Extensions.DependencyInjection.IServiceCollection AddJsonRepair(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action<Penghou.Nuwa.JsonRepairOptions>)
        type Penghou.Nuwa.IJsonRepairPipeline : interface
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.JsonRepairResult> RepairAsync(System.String, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
          method System.Collections.Generic.IAsyncEnumerable<Penghou.Nuwa.JsonRepairStreamEvent> RepairStreamAsync(System.Collections.Generic.IAsyncEnumerable<System.String>, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.JsonRepair : static class
          method static System.Threading.Tasks.ValueTask<Penghou.Nuwa.JsonRepairResult> RepairAsync(System.String, Penghou.Nuwa.JsonSchemaExpectation, System.Action<Penghou.Nuwa.JsonRepairOptions>, System.Threading.CancellationToken)
        type Penghou.Nuwa.JsonRepairLimitException : sealed class : System.Exception, System.Runtime.Serialization.ISerializable
          ctor(System.String)
        type Penghou.Nuwa.JsonRepairLimits : sealed class : System.IEquatable<Penghou.Nuwa.JsonRepairLimits>
          ctor()
          property Penghou.Nuwa.JsonRepairLimits Default
          property System.Int32 MaxCorrections
          property System.Int32 MaxDepth
          property System.Int32 MaxInputLength
          property System.Int32 MaxOutputLength
        type Penghou.Nuwa.JsonRepairOptions : sealed class
          ctor()
          property System.Boolean AllowTruncationSalvage
          property Penghou.Nuwa.JsonRepairLimits Limits
          property System.Collections.Generic.IReadOnlyList<System.Type> NodeRepairs
          property System.Collections.Generic.IReadOnlyList<System.Type> SalvageRepairs
          property System.Collections.Generic.IReadOnlyList<System.Type> TextRepairs
          method Penghou.Nuwa.JsonRepairOptions AddNodeRepair<T>()
          method Penghou.Nuwa.JsonRepairOptions AddSalvageRepair<T>()
          method Penghou.Nuwa.JsonRepairOptions AddTextRepair<T>()
          method Penghou.Nuwa.JsonRepairOptions ClearNodeRepairs()
          method Penghou.Nuwa.JsonRepairOptions ClearTextRepairs()
          method Penghou.Nuwa.JsonRepairOptions DisableSalvageFallback()
          method Penghou.Nuwa.JsonRepairOptions EnableRequiredPropertyReconciliation()
          method Penghou.Nuwa.JsonRepairOptions EnableSchemaCoercions()
          method Penghou.Nuwa.JsonRepairOptions EnableStructuralPropertyReconciliation()
          method Penghou.Nuwa.JsonRepairOptions InsertNodeRepairAfter<TAnchor, TNew>()
          method Penghou.Nuwa.JsonRepairOptions InsertSalvageRepairAfter<TAnchor, TNew>()
          method Penghou.Nuwa.JsonRepairOptions InsertTextRepairAfter<TAnchor, TNew>()
          method Penghou.Nuwa.JsonRepairOptions RemoveNodeRepair<T>()
          method Penghou.Nuwa.JsonRepairOptions RemoveSalvageRepair<T>()
          method Penghou.Nuwa.JsonRepairOptions RemoveTextRepair<T>()
        type Penghou.Nuwa.JsonRepairPipeline : sealed class : Penghou.Nuwa.IJsonRepairPipeline
          ctor(System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.ITextRepair>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.ITextRepair>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.INodeRepair>, Microsoft.Extensions.Logging.ILogger<Penghou.Nuwa.JsonRepairPipeline>)
          ctor(System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.ITextRepair>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.ITextRepair>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.INodeRepair>, Microsoft.Extensions.Logging.ILogger<Penghou.Nuwa.JsonRepairPipeline>, Penghou.Nuwa.JsonRepairLimits)
          ctor(System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.ITextRepair>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.ITextRepair>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.Strategies.INodeRepair>, Microsoft.Extensions.Logging.ILogger<Penghou.Nuwa.JsonRepairPipeline>, Penghou.Nuwa.JsonRepairLimits, System.Boolean)
          method static Penghou.Nuwa.JsonRepairPipeline Create(System.Action<Penghou.Nuwa.JsonRepairOptions>)
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.JsonRepairResult> RepairAsync(System.String, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
          method System.Collections.Generic.IAsyncEnumerable<Penghou.Nuwa.JsonRepairStreamEvent> RepairStreamAsync(System.Collections.Generic.IAsyncEnumerable<System.String>, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.JsonRepairResult : sealed class : System.IDisposable
          ctor(System.Text.Json.JsonDocument, System.Text.Json.Nodes.JsonNode, System.String, System.String, System.Boolean, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport>)
          property System.Double Confidence
          property System.Text.Json.JsonDocument Document
          property System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport> NodeRepairs
          property System.String OriginalText
          property System.String RepairedText
          property System.Text.Json.Nodes.JsonNode Root
          property System.Collections.Generic.IReadOnlyList<System.String> ShapeErrors
          property Penghou.Nuwa.JsonRepairShapeStatus ShapeStatus
          property System.Boolean Succeeded
          property Penghou.Nuwa.StrategyReport SucceededBy
          property System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport> TextRepairs
          property Penghou.Nuwa.TolerantRecoveryReport TolerantRecovery
          property System.Boolean WasRepaired
          method System.Void Dispose()
          method static Penghou.Nuwa.JsonRepairResult Failure(System.String, System.String, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport>)
          method System.Text.Json.JsonDocument GetDocumentOrThrow()
          method System.String GetRepairedTextOrThrow()
          method System.Text.Json.Nodes.JsonNode GetRootOrThrow()
          method System.Boolean IsConfident(System.Double)
          method static Penghou.Nuwa.JsonRepairResult Success(System.Text.Json.Nodes.JsonNode, System.String, System.String, System.Boolean, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport>, System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.StrategyReport>)
        type Penghou.Nuwa.JsonRepairShapeStatus : enum : System.Enum, System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable
          value NotEvaluated
          value Matched
          value Mismatched
        type Penghou.Nuwa.JsonRepairStreamCompleted : sealed class : Penghou.Nuwa.JsonRepairStreamEvent, System.IEquatable<Penghou.Nuwa.JsonRepairStreamCompleted>, System.IEquatable<Penghou.Nuwa.JsonRepairStreamEvent>
          ctor(Penghou.Nuwa.JsonRepairResult)
          property Penghou.Nuwa.JsonRepairResult Result
        type Penghou.Nuwa.JsonRepairStreamDelta : sealed class : Penghou.Nuwa.JsonRepairStreamEvent, System.IEquatable<Penghou.Nuwa.JsonRepairStreamDelta>, System.IEquatable<Penghou.Nuwa.JsonRepairStreamEvent>
          ctor(System.Int32, System.String)
          property System.Int32 Offset
          property System.String Text
        type Penghou.Nuwa.JsonRepairStreamEvent : abstract class : System.IEquatable<Penghou.Nuwa.JsonRepairStreamEvent>
        type Penghou.Nuwa.JsonSchemaBranch : sealed class : System.IEquatable<Penghou.Nuwa.JsonSchemaBranch>
          ctor(Penghou.Nuwa.JsonSchemaExpectation, System.String, System.Collections.Generic.IReadOnlySet<System.String>)
          property System.String DiscriminatorProperty
          property System.Collections.Generic.IReadOnlySet<System.String> DiscriminatorValues
          property Penghou.Nuwa.JsonSchemaExpectation Expectation
        type Penghou.Nuwa.JsonSchemaExpectation : sealed class : System.IEquatable<Penghou.Nuwa.JsonSchemaExpectation>
          ctor(System.Collections.Generic.IReadOnlyDictionary<System.String, Penghou.Nuwa.JsonSchemaFieldKind>, System.Text.Json.Nodes.JsonNode, System.Boolean)
          property System.Collections.Generic.IReadOnlyList<Penghou.Nuwa.JsonSchemaBranch> Branches
          property System.Nullable<Penghou.Nuwa.JsonSchemaFieldKind> ExpectedKind
          property System.Boolean Nullable
          property System.Collections.Generic.IReadOnlyDictionary<System.String, Penghou.Nuwa.JsonSchemaFieldKind> PropertyKinds
          property System.Text.Json.Nodes.JsonNode Schema
          method static Penghou.Nuwa.JsonSchemaExpectation FromSchemaJson(System.String)
          method static Penghou.Nuwa.JsonSchemaExpectation FromSchemaNode(System.Text.Json.Nodes.JsonNode)
          method static Penghou.Nuwa.JsonSchemaExpectation FromType<T>()
          method static Penghou.Nuwa.JsonSchemaExpectation FromType<T>(System.Text.Json.JsonSerializerOptions)
          method static Penghou.Nuwa.JsonSchemaExpectation FromType(System.Type)
          method static Penghou.Nuwa.JsonSchemaExpectation FromType(System.Type, System.Text.Json.JsonSerializerOptions)
          method Penghou.Nuwa.JsonSchemaExpectation GetItem()
          method Penghou.Nuwa.JsonSchemaExpectation GetProperty(System.String)
          method System.Collections.Generic.IReadOnlySet<System.String> GetStringPropertyNames()
          method System.Collections.Generic.IReadOnlyList<System.String> ValidateShape(System.Text.Json.Nodes.JsonNode)
          method System.Collections.Generic.IReadOnlyList<System.String> Validate(System.Text.Json.Nodes.JsonNode)
        type Penghou.Nuwa.JsonSchemaFieldKind : enum : System.Enum, System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable
          value String
          value Number
          value Boolean
          value Object
          value Array
        type Penghou.Nuwa.NodeRepairAttempt : struct : System.IEquatable<Penghou.Nuwa.NodeRepairAttempt>
          ctor(Penghou.Nuwa.RepairOutcome, System.Text.Json.Nodes.JsonNode, System.String)
          property System.String Note
          property Penghou.Nuwa.RepairOutcome Outcome
          property System.Text.Json.Nodes.JsonNode Repaired
        type Penghou.Nuwa.RepairOutcome : enum : System.Enum, System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable
          value NotApplicable
          value Failed
          value Repaired
        type Penghou.Nuwa.Strategies.ConcatenatedJsonExtractionStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.INodeRepair : interface
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.ITextRepair : interface
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.MarkdownJsonFenceRepairStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.ProseWrapperExtractionStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.PseudoCSharpVerbatimStringRepairStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.PseudoJavaScriptTemplateStringRepairStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SalvageRepairStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
          method static System.String TryRepair(System.String)
        type Penghou.Nuwa.Strategies.SchemaGuidedArrayWrapStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedEnumFuzzyMatchStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedJsonStringExpansionStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedOptionalNullRemovalStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedRequiredPropertyReconciliationStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedStringToBooleanCoercionStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedStringToNumberCoercionStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedStructuralPropertyReconciliationStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.SchemaGuidedUnknownPropertyPruneStrategy : sealed class : Penghou.Nuwa.Strategies.INodeRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.NodeRepairAttempt> RepairAsync(System.Text.Json.Nodes.JsonNode, Penghou.Nuwa.JsonSchemaExpectation, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.UnicodeDelimiterNormalizationStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.Strategies.XmlWrappedExtractionStrategy : sealed class : Penghou.Nuwa.Strategies.ITextRepair
          ctor()
          property System.String Name
          method System.Threading.Tasks.ValueTask<Penghou.Nuwa.TextRepairAttempt> RepairAsync(System.String, System.Threading.CancellationToken)
        type Penghou.Nuwa.StrategyReport : sealed class : System.IEquatable<Penghou.Nuwa.StrategyReport>
          ctor(System.String, Penghou.Nuwa.StrategyStatus, System.String, System.String)
          property System.String Name
          property System.String Note
          property System.String Repaired
          property Penghou.Nuwa.StrategyStatus Status
        type Penghou.Nuwa.StrategyStatus : enum : System.Enum, System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable
          value Skipped
          value NotApplicable
          value Failed
          value Succeeded
        type Penghou.Nuwa.TextRepairAttempt : struct : System.IEquatable<Penghou.Nuwa.TextRepairAttempt>
          ctor(Penghou.Nuwa.RepairOutcome, System.String, System.String)
          property System.String Note
          property Penghou.Nuwa.RepairOutcome Outcome
          property System.String Repaired
        type Penghou.Nuwa.TolerantRecoveryReport : sealed class : System.IEquatable<Penghou.Nuwa.TolerantRecoveryReport>
          ctor(System.Boolean, System.String, System.Int32, System.Int32, System.Collections.Generic.IReadOnlyList<System.String>)
          property System.Int32 CorrectionCount
          property System.Collections.Generic.IReadOnlyList<System.String> Corrections
          property System.String Outcome
          property System.Int32 SchemaGuidedStringCorrectionCount
          property System.Boolean Succeeded
        """;

    private static string GeneratePublicApiSnapshot()
    {
        var builder = new StringBuilder();

        foreach (var type in typeof(JsonRepair)
                     .Assembly
                     .GetExportedTypes()
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine(DescribeType(type));

            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type))
                {
                    builder.AppendLine($"  value {name}");
                }
            }
            else
            {
                foreach (var member in DescribeMembers(type))
                {
                    builder.AppendLine(member);
                }
            }
        }

        return builder.ToString().Replace("\r\n", "\n");
    }

    private static string DescribeType(Type type)
    {
        var kind = type switch
        {
            _ when type.IsInterface =>
                "interface",
            _ when type.IsEnum =>
                "enum",
            _ when type.IsValueType =>
                "struct",
            _ when type.IsAbstract && type.IsSealed =>
                "static class",
            _ when type.IsSealed =>
                "sealed class",
            _ when type.IsAbstract =>
                "abstract class",
            _ =>
                "class"
        };

        var bases = new List<string>();
        var baseType = type.BaseType;

        if (baseType is not null &&
            baseType != typeof(object) &&
            baseType != typeof(ValueType))
        {
            bases.Add(DescribeTypeName(baseType));
        }

        bases.AddRange(
            type
                .GetInterfaces()
                .OrderBy(item => item.FullName, StringComparer.Ordinal)
                .Select(DescribeTypeName));

        var basePart =
            bases.Count > 0
                ? $" : {string.Join(", ", bases)}"
                : string.Empty;

        return $"type {type.FullName} : {kind}{basePart}";
    }

    private static IEnumerable<string> DescribeMembers(Type type)
    {
        var members = new List<string>();

        foreach (var constructor in type
                     .GetConstructors(
                         BindingFlags.Instance | BindingFlags.Public)
                     .OrderBy(
                         constructor =>
                             string.Join(
                                 ",",
                                 constructor
                                     .GetParameters()
                                     .Select(parameter => parameter.ParameterType.FullName))))
        {
            members.Add(
                $"  ctor({DescribeParameters(constructor.GetParameters())})");
        }

        foreach (var property in type
                     .GetProperties(
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.DeclaredOnly)
                     .Where(IsNotRecordBoilerplate)
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var indexer = property.GetIndexParameters();
            var signature = indexer.Length > 0
                ? $"this[{DescribeParameters(indexer)}]"
                : property.Name;

            members.Add(
                $"  property {DescribeTypeName(property.PropertyType)} {signature}");
        }

        foreach (var field in type
                     .GetFields(
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.DeclaredOnly)
                     .OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            var modifiers =
                field.IsStatic
                    ? "static "
                    : string.Empty;

            members.Add(
                $"  field {modifiers}{DescribeTypeName(field.FieldType)} {field.Name}");
        }

        foreach (var method in type
                     .GetMethods(
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.DeclaredOnly)
                     .Where(IsNotRecordBoilerplate)
                     .Where(IsNotAccessor)
                     .OrderBy(
                         method =>
                             $"{method.Name}|{DescribeMethodName(method)}|{DescribeParameters(method.GetParameters())}",
                         StringComparer.Ordinal))
        {
            var modifiers =
                method.IsStatic
                    ? "static "
                    : string.Empty;

            members.Add(
                $"  method {modifiers}{DescribeTypeName(method.ReturnType)} {DescribeMethodName(method)}({DescribeParameters(method.GetParameters())})");
        }

        return members;
    }

    private static string DescribeMethodName(MethodInfo method)
    {
        if (!method.IsGenericMethod)
            return method.Name;

        return
            $"{method.Name}<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>";
    }

    private static string DescribeParameters(
        IEnumerable<ParameterInfo> parameters) =>
        string.Join(
            ", ",
            parameters.Select(
                parameter =>
                    DescribeTypeName(parameter.ParameterType)));

    private static string DescribeTypeName(Type type)
    {
        if (type.IsGenericParameter)
            return type.Name;

        if (type.IsArray)
            return $"{DescribeTypeName(type.GetElementType()!)}[]";

        if (type.IsByRef)
            return DescribeTypeName(type.GetElementType()!);

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = definition.FullName ?? definition.Name;
            var tick = name.IndexOf('`');

            if (tick >= 0)
            {
                name = name[..tick];
            }

            var arguments = string.Join(
                ", ",
                type.GetGenericArguments().Select(DescribeTypeName));

            return $"{name}<{arguments}>";
        }

        return type.FullName ?? type.Name;
    }

    private static bool IsNotRecordBoilerplate(MemberInfo member) =>
        member.Name is not (
            "Equals" or
            "GetHashCode" or
            "ToString" or
            "PrintMembers" or
            "Deconstruct" or
            "op_Equality" or
            "op_Inequality" or
            "<Clone>$");

    private static bool IsNotAccessor(MethodInfo method) =>
        !method.Name.StartsWith("get_", StringComparison.Ordinal) &&
        !method.Name.StartsWith("set_", StringComparison.Ordinal) &&
        !method.Name.StartsWith("add_", StringComparison.Ordinal) &&
        !method.Name.StartsWith("remove_", StringComparison.Ordinal);
}
