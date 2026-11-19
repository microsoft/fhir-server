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

            if (tokens.Length >= 7 && tokens[6] is IncludesContinuationToken)
            {
                tokens[6] = ((IncludesContinuationToken)tokens[6]).ToString();
            }

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

        /// <summary>
        /// Gets the cursor used to select the outer page's matches, before paging included resources.
        /// A null cursor selects the beginning of the recorded sort phase.
        /// </summary>
        public string MatchContinuationToken { get; private set; }

        /// <summary>
        /// Gets the number of matches in the recorded sort phase of the outer page.
        /// When present, replay that page instead of using a surrogate ID interval.
        /// </summary>
        public int? MatchPageSize { get; private set; }

        /// <summary>
        /// Advances the include cursor without changing the outer page scope.
        /// </summary>
        /// <param name="resourceTypeId">The include cursor's resource type.</param>
        /// <param name="resourceSurrogateId">The include cursor's surrogate ID.</param>
        /// <returns>A token with the updated include cursor.</returns>
        public IncludesContinuationToken WithIncludeCursor(short resourceTypeId, long resourceSurrogateId)
        {
            var tokens = (object[])_tokens.Clone();
            Array.Resize(ref tokens, Math.Max(tokens.Length, 5));
            tokens[3] = resourceTypeId;
            tokens[4] = resourceSurrogateId;
            return new IncludesContinuationToken(tokens);
        }

        /// <summary>
        /// Preserves the remaining sort phase while paging includes from the first phase.
        /// </summary>
        /// <param name="secondPhaseToken">The remaining phase's includes token.</param>
        /// <returns>A token containing both phases.</returns>
        public IncludesContinuationToken WithSecondPhase(IncludesContinuationToken secondPhaseToken)
        {
            var tokens = (object[])_tokens.Clone();
            Array.Resize(ref tokens, Math.Max(tokens.Length, 7));
            tokens[5] = SortQuerySecondPhase ?? false;
            tokens[6] = secondPhaseToken;
            return new IncludesContinuationToken(tokens);
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

        /// <summary>
        /// Initializes the continuation token from an array of tokens.
        /// The tokens are expected to be in the following order:
        /// 1. MatchResourceTypeId (short):                                 The resource type ID of the matched resources.
        /// 2. MatchResourceSurrogateIdMin (long):                          The minimum surrogate ID of the matched resources.
        /// 3. MatchResourceSurrogateIdMax (long):                          The maximum surrogate ID of the matched resources.
        /// 4. IncludeResourceTypeId (short?):                              The resource type ID of the included resources.
        /// 5. IncludeResourceSurrogateId (long?):                          The minimum surrogate ID of the included resources.
        /// 6. SortQuerySecondPhase (bool?):                                Indicates if the sort query is in the second phase.
        /// 7. SecondPhaseContinuationToken (IncludesContinuationToken?):   The continuation token for the second phase of the sort query. This is provided if the matched resources that generated this token were from both the first and second phases of a sort query.
        /// 8. MatchContinuationToken (string?):                           The input cursor for replaying a non-surrogate-sorted page.
        /// 9. MatchPageSize (int):                                        The actual match count for that page's sort phase, excluding lookahead.
        ///
        /// Tokens 1-3 are required, tokens 4-9 are optional.
        /// 5 is required if 4 is present.
        /// 8 and 9 must be supplied together.
        /// </summary>
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
                            else if ((_tokens.Length == 7 || _tokens.Length == 9)
                                && bool.TryParse(_tokens[5]?.ToString(), out sortQuerySecondPhase))
                            {
                                SortQuerySecondPhase = sortQuerySecondPhase;
                                SecondPhaseContinuationToken = FromString((string)_tokens[6]);

                                if (_tokens.Length == 9)
                                {
                                    if ((_tokens[7] == null || _tokens[7] is string)
                                        && int.TryParse(_tokens[8]?.ToString(), out var pageSize)
                                        && pageSize > 0)
                                    {
                                        MatchContinuationToken = (string)_tokens[7];
                                        MatchPageSize = pageSize;
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
