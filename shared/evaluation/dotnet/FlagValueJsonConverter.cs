using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FutureFlags.Evaluation;

/// <summary>
/// Reads and writes a <see cref="FlagValue"/> as bare JSON.
///
/// <para>
/// No discriminator, for the same reason <see cref="AttributeValueJsonConverter"/> has none: JSON's
/// own token types already tell the four kinds apart unambiguously, and a tagged form would be a
/// second thing to keep in step across three runtimes. It also means an OpenFeature client reading
/// our payloads sees exactly the <c>value</c> the OFREP schema describes — a bare boolean, string,
/// number, or object — rather than a FutureFlags-shaped wrapper it would have to unwrap.
/// </para>
/// <para>
/// An object or array becomes <see cref="FlagValueKind.Object"/>, carrying its raw text. That is
/// the one case where the token type maps to a kind rather than being refused, and it is why this
/// converter is not simply a copy of the attribute one.
/// </para>
/// </summary>
public sealed class FlagValueJsonConverter : JsonConverter<FlagValue>
{
    /// <summary>Reads a flag value from bare JSON.</summary>
    public override FlagValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return FlagValue.True;

            case JsonTokenType.False:
                return FlagValue.False;

            case JsonTokenType.String:
                return FlagValue.OfString(reader.GetString());

            case JsonTokenType.Number:
                return FlagValue.OfNumber(reader.GetDouble());

            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    return FlagValue.OfObject(document.RootElement.GetRawText());
                }

            default:
                throw new JsonException(
                    "A flag value must be true/false, a string, a number, an object, or an array.");
        }
    }

    /// <summary>Writes the bare JSON for this value.</summary>
    public override void Write(Utf8JsonWriter writer, FlagValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case FlagValueKind.String:
                writer.WriteStringValue(value.Text);
                break;

            case FlagValueKind.Number:
                writer.WriteNumberValue(value.Number);
                break;

            case FlagValueKind.Object:
                // Written raw rather than reparsed into a document and re-emitted, so that a value
                // round-trips byte-for-byte. The ruleset fingerprint is taken over this same text,
                // so any normalisation here would move an ETag without the flag having changed.
                writer.WriteRawValue(value.Text);
                break;

            default:
                writer.WriteBooleanValue(value.Boolean);
                break;
        }
    }
}
