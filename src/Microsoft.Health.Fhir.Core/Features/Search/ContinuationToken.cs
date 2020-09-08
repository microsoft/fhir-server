// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents the search continuation token.
    /// </summary>
    public class ContinuationToken
    {
        // the token is an array.
        private object[] _tokens;

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

        // Currently only a single sort is implemented
        public string SortExpr
        {
            get
            {
                return _tokens.Length > 1 ? _tokens[0] as string : null;
            }

            set
            {
                if (_tokens.Length == 1)
                {
                    _tokens = new object[] { value, _tokens[0] };
                }
                else
                {
                    _tokens[0] = value;
                }
            }
        }

        public string ToJson()
        {
            return JArray.FromObject(_tokens).ToString();
        }

        public override string ToString()
        {
            return ToJson();
        }

        public static ContinuationToken FromString(string json)
        {
            try
            {
                bool success = true;
                var settings = new JsonSerializerSettings
                {
                    Error = (sender, args) =>
                    {
                        success = false;
                        args.ErrorContext.Handled = true;
                    },
                    MissingMemberHandling = MissingMemberHandling.Error,
                };
                var result = JsonConvert.DeserializeObject<object[]>(json, settings);
                if (success)
                {
                    return new ContinuationToken(result);
                }
                else
                {
                    // backward compatibilty support
                    if (long.TryParse(json, NumberStyles.None, CultureInfo.InvariantCulture, out var sid))
                    {
                        result = new object[] { sid };
                        return new ContinuationToken(result);
                    }
                    else
                    {
                        throw new Exception("Continuation Token is malformed.");
                    }
                }
            }
            catch
            {
                throw new Exception("Continuation Token is malformed.");
            }
        }
    }
}
