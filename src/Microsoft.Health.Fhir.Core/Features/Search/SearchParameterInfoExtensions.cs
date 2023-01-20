// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Text;
using EnsureThat;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public static class SearchParameterInfoExtensions
    {
        /// <summary>
        /// Given a list of <see cref="SearchParameterInfo"/> calculates a hash using the
        /// <see cref="SearchParameterInfo.Url"/>, <see cref="SearchParameterInfo.Type"/>,
        /// <see cref="SearchParameterInfo.Expression"/>, <see cref="SearchParameterInfo.TargetResourceTypes"/>, and
        /// <see cref="SearchParameterInfo.BaseResourceTypes"/>,
        /// values of each component. The same collection of search parameter infos (irrespective of their order in the input)
        /// will return the same hash.
        /// </summary>
        /// <param name="searchParamaterInfos">A list of <see cref="SearchParameterInfo" /></param>
        /// <returns>A hash based on the search parameter uri and last updated value.</returns>
        internal static string CalculateSearchParameterHash(this IEnumerable<SearchParameterInfo> searchParamaterInfos)
        {
            EnsureArg.IsNotNull(searchParamaterInfos, nameof(searchParamaterInfos));
            searchParamaterInfos = searchParamaterInfos.ToList();
            EnsureArg.IsGt(searchParamaterInfos.Count(), 0, nameof(searchParamaterInfos));

            var sb = new StringBuilder();
            foreach (SearchParameterInfo searchParamInfo in searchParamaterInfos.OrderBy(x => x.Url.ToString()))
            {
                sb.Append(searchParamInfo.Url);
                sb.Append(searchParamInfo.Type);
                sb.Append(searchParamInfo.Expression);

                if (searchParamInfo.SortStatus != SortParameterStatus.Disabled)
                {
                    sb.Append("sortable");
                }

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
            }

            string hash = sb.ToString().ComputeHash();
            return hash;
        }
    }
}
