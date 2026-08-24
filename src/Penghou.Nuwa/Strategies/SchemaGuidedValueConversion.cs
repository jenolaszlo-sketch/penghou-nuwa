using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

internal static class SchemaGuidedValueConversion
{
    public static bool TryPrepareValue(
        JsonNode value,
        JsonSchemaExpectation expectation,
        out JsonNode prepared)
    {
        if (expectation.ValidateShape(value).Count == 0)
        {
            prepared = value.DeepClone();
            return true;
        }

        if (value is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text))
        {
            if (expectation.ExpectedKind == JsonSchemaFieldKind.Number &&
                SchemaGuidedStringToNumberCoercionStrategy.TryCoerceNumber(
                    text,
                    out var number) &&
                expectation.ValidateShape(number).Count == 0)
            {
                prepared = number;
                return true;
            }

            if (expectation.ExpectedKind == JsonSchemaFieldKind.Boolean &&
                SchemaGuidedStringToBooleanCoercionStrategy.TryCoerceBoolean(
                    text,
                    out var boolean) &&
                expectation.ValidateShape(boolean).Count == 0)
            {
                prepared = boolean;
                return true;
            }
        }

        prepared = null!;
        return false;
    }
}
