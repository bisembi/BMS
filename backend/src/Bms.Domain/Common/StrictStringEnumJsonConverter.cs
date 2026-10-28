using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bms.Domain.Common;

public sealed class StrictStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"{typeof(TEnum).Name} must be an exact string code.");
        }

        var code = reader.GetString();
        if (code is null ||
            !Enum.TryParse<TEnum>(code, ignoreCase: false, out var value) ||
            !Enum.IsDefined(value) ||
            !string.Equals(value.ToString(), code, StringComparison.Ordinal))
        {
            throw new JsonException($"Unsupported {typeof(TEnum).Name} code.");
        }

        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException($"Unsupported {typeof(TEnum).Name} value.");
        }

        writer.WriteStringValue(value.ToString());
    }
}
