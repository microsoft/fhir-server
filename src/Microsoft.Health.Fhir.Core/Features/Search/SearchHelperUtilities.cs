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
    public static class SearchHelperUtilities
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
        public static string CalculateSearchParameterHash(IEnumerable<SearchParameterInfo> searchParamaterInfos)
        {
            EnsureArg.IsNotNull(searchParamaterInfos, nameof(searchParamaterInfos));
            EnsureArg.IsGt(searchParamaterInfos.Count(), 0, nameof(searchParamaterInfos));

            StringBuilder sb = new StringBuilder();
            foreach (SearchParameterInfo searchParamInfo in searchParamaterInfos.OrderBy(x => x.Url.ToString()))
            {
                sb.Append(searchParamInfo.Url.ToString());
                sb.Append(searchParamInfo.Type.ToString());
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
            }

            string hash = sb.ToString().ComputeHash();
            return hash;
        }
    }
}
