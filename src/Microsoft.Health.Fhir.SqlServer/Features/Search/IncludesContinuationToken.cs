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

        /// <summary>
        /// When a search with sort is run there are two cases for how the search is handled: looking for results with the sort value or results without the sort value.
        /// In assending sort the first phase looks for results without the sort value and the second phase looks for results with the sort value.
        /// In descending sort the first phase looks for results with the sort value and the second phase looks for results without the sort value.
        /// This parameter indicates if the search that generated this continuation token was in its first or second phase so the includes results are for the correct matched results.
        /// </summary>
        public bool? SortQuerySecondPhase
        {
            get;
            private set;
        }

        public IncludesContinuationToken SecondPhaseContinuationToken
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
                MatchResourceSurrogateIdMin = sid0 < sid1 ? sid0 : sid1;
                MatchResourceSurrogateIdMax = sid0 < sid1 ? sid1 : sid0;
                initialized = true;

                if (_tokens.Length > 3)
                {
                    if (_tokens.Length >= 5)
                    {
                        IncludeResourceTypeId = short.TryParse(_tokens[3]?.ToString(), out tid) ? tid : null;
                        IncludeResourceSurrogateId = long.TryParse(_tokens[4]?.ToString(), out sid0) ? sid0 : null;

                        if (_tokens.Length > 5)
                        {
                            if (_tokens.Length == 6
                                && bool.TryParse(_tokens[5]?.ToString(), out var sortQuerySecondPhase))
                            {
                                SortQuerySecondPhase = sortQuerySecondPhase;
                            }
                            else if (_tokens.Length == 7
                                && bool.TryParse(_tokens[5]?.ToString(), out sortQuerySecondPhase))
                            {
                                SortQuerySecondPhase = sortQuerySecondPhase;
                                SecondPhaseContinuationToken = FromString(_tokens[6]?.ToString());
                            }
                            else
                            {
                                initialized = false;
                            }
                        }
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
