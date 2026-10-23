// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EnsureThat;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public static class SearchParameterInfoExtensions
    {
        /// <summary>
        /// Calculates an order-independent hash of the supported or enabled search parameters' URL, type, expression,
        /// target and base resource types, and vector settings (extraction policy, max input tokens, minimum score, and chunk overrides).
        /// </summary>
        /// <param name="searchParamaterInfos">The search parameters to hash; must not be empty.</param>
        /// <returns>A hash that changes when any hashed property of a supported or enabled search parameter changes.</returns>
        internal static string CalculateSearchParameterHash(this IEnumerable<SearchParameterInfo> searchParamaterInfos)
        {
            EnsureArg.IsNotNull(searchParamaterInfos, nameof(searchParamaterInfos));
            searchParamaterInfos = searchParamaterInfos.ToList();
            EnsureArg.IsGt(searchParamaterInfos.Count(), 0, nameof(searchParamaterInfos));

            var sb = new StringBuilder();
            foreach (var searchParamInfo in searchParamaterInfos.Where(_ => _.SearchParameterStatus == SearchParameterStatus.Supported
                                                                            || _.SearchParameterStatus == SearchParameterStatus.Enabled)
                                                                .OrderBy(x => x.Url.ToString()))
            {
                sb.Append(searchParamInfo.Url);
                sb.Append(searchParamInfo.Type);
                sb.Append(searchParamInfo.Expression);

                if (searchParamInfo.TargetResourceTypes != null &&
                    searchParamInfo.TargetResourceTypes.Any())
                {
                    sb.Append(string.Join(null, searchParamInfo.TargetResourceTypes.OrderBy(s => s)));
                }

                if (searchParamInfo.BaseResourceTypes != null &&
                    searchParamInfo.BaseResourceTypes.Any())
                {
                    sb.Append(string.Join(null, searchParamInfo.BaseResourceTypes.OrderBy(s => s)));
                }

                if (searchParamInfo.VectorConfig is { } vectorConfig)
                {
                    // Delimit nullable chunk overrides so absent, explicit, and adjacent values cannot collide.
                    sb.Append(
                        CultureInfo.InvariantCulture,
                        $"|vector|{vectorConfig.ExtractionPolicy}|{vectorConfig.MaxInputTokens}|{vectorConfig.MinimumScore}|{FormatOptional(vectorConfig.ChunkSizeTokens)}|{FormatOptional(vectorConfig.ChunkOverlapTokens)}");
                }
            }

            string hash = sb.ToString().ComputeHash();
            return hash;
        }

        private static string FormatOptional(int? value)
        {
            return value?.ToString(CultureInfo.InvariantCulture) ?? "default";
        }

        public static bool IsBaseTypeSearchParameter(this SearchParameterInfo searchParameter)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));

            var resourceType = (searchParameter.BaseResourceTypes?.Any() ?? false) ? searchParameter.BaseResourceTypes[0] : null;
            return (string.Equals(resourceType, KnownResourceTypes.Resource, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.OrdinalIgnoreCase))
                && (searchParameter.Code?.StartsWith('_') ?? false);
        }
    }
}
