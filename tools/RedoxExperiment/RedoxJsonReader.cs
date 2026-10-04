using System.Globalization;
using System.Numerics;
using Newtonsoft.Json;
using REDox;
using REDox.Json;

namespace RedoxExperiment;

internal sealed class RedoxJsonReader : JsonReader
{
    private readonly JsonDocument _document;
    private readonly IEnumerator<(JsonToken Token, object? Value)> _tokens;

    public RedoxJsonReader(string json)
    {
        _document = JsonDocument.Parse(json, options: new JsonDocumentOptions
        {
            EnableValueValidation = true,
            MaxDepth = 64,
        });
        _tokens = Enumerate(_document.RootElement).GetEnumerator();
        DateParseHandling = DateParseHandling.None;
        FloatParseHandling = FloatParseHandling.Decimal;
    }

    public override bool Read()
    {
        if (!_tokens.MoveNext())
        {
            return false;
        }

        SetToken(_tokens.Current.Token, _tokens.Current.Value);
        return true;
    }

    public override void Close()
    {
        _tokens.Dispose();
        _document.Dispose();
        base.Close();
    }

    private static IEnumerable<(JsonToken Token, object? Value)> Enumerate(DElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                yield return (JsonToken.StartObject, null);
                foreach (var property in element.EnumerateObject())
                {
                    yield return (JsonToken.PropertyName, property.Name);
                    foreach (var token in Enumerate(property.Value))
                    {
                        yield return token;
                    }
                }

                yield return (JsonToken.EndObject, null);
                break;
            case JsonValueKind.Array:
                yield return (JsonToken.StartArray, null);
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var token in Enumerate(item))
                    {
                        yield return token;
                    }
                }

                yield return (JsonToken.EndArray, null);
                break;
            case JsonValueKind.String:
                yield return (JsonToken.String, element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.Token.Kind == DTokenKind.Integer)
                {
                    yield return (JsonToken.Integer, element.TryGetInt64(out var integer)
                        ? (object)integer
                        : BigInteger.Parse(element.ToJsonString(), CultureInfo.InvariantCulture));
                }
                else
                {
                    yield return (JsonToken.Float, element.GetDecimal());
                }

                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                yield return (JsonToken.Boolean, element.GetBoolean());
                break;
            case JsonValueKind.Null:
                yield return (JsonToken.Null, null);
                break;
            default:
                throw new JsonReaderException($"Unsupported REDox token {element.ValueKind}.");
        }
    }
}
