// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text.Json;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents the search continuation token.
    /// </summary>
    public class ContinuationToken
    {
        // the token is an array.
        private object[] _tokens;
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions() { Converters = { new ContinuationTokenConverter() } };

        public ContinuationToken(object[] tokens)
        {
            _tokens = tokens;
        }

        public long ResourceSurrogateId
        {
            get
            {
                return (long)_tokens[^1];
            }

            set
            {
                _tokens[^1] = value;
            }
        }

        public short? ResourceTypeId
        {
            get
            {
                if (_tokens.Length < 2)
                {
                    return null;
                }

                return _tokens[^2] switch
                {
                    short s => s,
                    long l => (short)l, // deserialization from JSON creates longs
                    _ => null,
                };
            }

            set
            {
                _tokens[^2] = value;
            }
        }

        // Currently only a single sort is implemented
        public string SortValue
        {
            get
            {
                return _tokens.Length > 1 ? _tokens[0] as string : null;
            }
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(_tokens);
        }

        public override string ToString()
        {
            return ToJson();
        }

        public static ContinuationToken FromString(string json)
        {
            if (json == null)
            {
                return null;
            }

            if (long.TryParse(json, NumberStyles.None, CultureInfo.InvariantCulture, out var sid))
            {
                return new ContinuationToken(new object[] { sid });
            }

            try
            {
                object[] result = JsonSerializer.Deserialize<object[]>(json, Options);
                return new ContinuationToken(result);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private class ContinuationTokenConverter : System.Text.Json.Serialization.JsonConverter<object>
        {
            public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetInt64(),
                _ => throw new NotSupportedException(),
            };

            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => throw new NotImplementedException();
        }
    }
}
