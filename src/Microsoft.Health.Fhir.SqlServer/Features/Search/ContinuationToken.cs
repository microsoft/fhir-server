// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
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

        internal bool TryGetSemanticCursor(out double distance, out short resourceTypeId, out long resourceSurrogateId)
        {
            distance = default;
            resourceTypeId = default;
            resourceSurrogateId = default;

            if (_tokens.Length != 3 ||
                _tokens[0] is not string distanceText ||
                !double.TryParse(distanceText, NumberStyles.Float, CultureInfo.InvariantCulture, out distance) ||
                !double.IsFinite(distance) ||
                _tokens[2] is not long parsedResourceSurrogateId)
            {
                return false;
            }

            resourceTypeId = _tokens[1] switch
            {
                short value => value,
                long value when value >= short.MinValue && value <= short.MaxValue => (short)value,
                _ => default,
            };

            if (resourceTypeId == default)
            {
                return false;
            }

            resourceSurrogateId = parsedResourceSurrogateId;
            return true;
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
    }
}
