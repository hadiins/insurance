using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aqsat.Infrastructure.Payments;

/// <summary>
/// Reads a JSON value that a PSP may send as either a string or a number into a string. Both
/// gateways are inconsistent about this in practice — زرینپال documents ref_id as an Integer while
/// the sample payloads elsewhere quote amounts as strings, and گویا پی does not state the type of
/// RefID at all. A plain string-typed property would throw JsonException on a numeric payload and
/// turn a SUCCESSFUL payment into a 500 the customer cannot recover from, so the coercion lives
/// here, once, instead of being rediscovered per provider.
///
/// null and JSON null both map to null; everything else is rendered invariantly, so a numeric
/// reference id never picks up a Persian-locale digit shape.
/// </summary>
internal sealed class JsonStringOrNumberConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString("R"),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
