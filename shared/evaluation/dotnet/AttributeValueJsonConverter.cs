using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatureFlags.Evaluation;

/// <summary>
/// Reads and writes an <see cref="AttributeValue"/> as a bare JSON primitive.
///
/// <para>
/// JSON already has exactly string, number, and boolean, and the closed set of attribute kinds is
/// that same set — so there is no discriminator. A tagged form
/// (<c>{"type":"number","value":18}</c>) would be more bytes on the wire and, worse, a second thing
/// to keep in step across three runtimes when the first one is already unambiguous.
/// </para>
/// <para>
/// <c>null</c> is deliberately not a case. An attribute whose value is unknown is an attribute that
/// is absent, and absent is already a non-match — inventing a fourth kind to mean the same thing
/// would give two spellings to one fact.
/// </para>
/// </summary>
public sealed class AttributeValueJsonConverter : JsonConverter<AttributeValue>
{
    /// <summary>
    /// Reads a bare JSON primitive. An array or an object is a <see cref="JsonException"/>, because
    /// there is no fourth kind for it to become.
    ///
    /// <para>
    /// <c>null</c> never reaches here: <see cref="AttributeValue"/> is a reference type, so unless a
    /// converter opts in via <c>HandleNull</c>, <c>System.Text.Json</c> short-circuits a null token
    /// itself and hands back a null <see cref="AttributeValue"/> without calling this method.
    /// This converter does not opt in, on purpose — that null propagates to exactly where the class
    /// doc above says it should mean the same thing as an absent attribute, and every consumer
    /// (<see cref="FlagContext"/>'s normalisation, a segment condition's own validation,
    /// <see cref="SegmentMatcher"/>) already drops it there rather than treating it as a fourth kind.
    /// The Node client makes the identical choice — see <c>normalizeContext</c>'s comment on why an
    /// unusable attribute value is dropped rather than rejected.
    /// </para>
    /// </summary>
    public override AttributeValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => AttributeValue.OfText(reader.GetString()),
            JsonTokenType.Number => AttributeValue.OfNumber(reader.GetDouble()),
            JsonTokenType.True => AttributeValue.OfBoolean(true),
            JsonTokenType.False => AttributeValue.OfBoolean(false),
            _ => throw new JsonException(
                "An attribute value must be a string, a number, or true/false."),
        };
    }

    /// <summary>Writes the bare JSON primitive for this value.</summary>
    public override void Write(Utf8JsonWriter writer, AttributeValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case AttributeValueKind.Text:
                writer.WriteStringValue(value.Text);
                break;

            case AttributeValueKind.Number:
                writer.WriteNumberValue(value.Number);
                break;

            default:
                writer.WriteBooleanValue(value.Boolean);
                break;
        }
    }
}
