// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public class IncludesContinuationToken
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions() { Converters = { new ContinuationTokenConverter() } };

        private readonly object[] _tokens;

        public IncludesContinuationToken(object[] tokens)
        {
            EnsureArg.IsNotNull(tokens, nameof(tokens));

            _tokens = tokens;
            Initialize();
        }

        public long MatchResourceSurrogateIdMax
        {
            get;
            private set;
        }

        public long MatchResourceSurrogateIdMin
        {
            get;
            private set;
        }

        public short MatchResourceTypeId
        {
            get;
            private set;
        }

        public long? IncludeResourceSurrogateId
        {
            get;
            private set;
        }

        public short? IncludeResourceTypeId
        {
            get;
            private set;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(_tokens);
        }

        public override string ToString()
        {
            return ToJson();
        }

        public static IncludesContinuationToken FromString(string json)
        {
            if (json == null)
            {
                return null;
            }

            try
            {
                object[] result = JsonSerializer.Deserialize<object[]>(json, Options);
                return new IncludesContinuationToken(result);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private void Initialize()
        {
            var initialized = false;
            if (_tokens?.Length >= 3
                && short.TryParse(_tokens[0]?.ToString(), out var tid)
                && long.TryParse(_tokens[1]?.ToString(), out var sid0)
                && long.TryParse(_tokens[2]?.ToString(), out var sid1))
            {
                MatchResourceTypeId = tid;
                MatchResourceSurrogateIdMin = sid0;
                MatchResourceSurrogateIdMax = sid1;
                initialized = true;

                if (_tokens.Length > 3)
                {
                    if (_tokens.Length == 5
                        && short.TryParse(_tokens[3]?.ToString(), out tid)
                        && long.TryParse(_tokens[4]?.ToString(), out sid0))
                    {
                        IncludeResourceTypeId = tid;
                        IncludeResourceSurrogateId = sid0;
                    }
                    else
                    {
                        initialized = false;
                    }
                }
            }

            if (!initialized)
            {
                throw new ArgumentException("Initialization failed due to invalid tokens.");
            }
        }
    }
}
