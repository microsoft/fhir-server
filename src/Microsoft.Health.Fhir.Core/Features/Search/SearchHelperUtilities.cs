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
        /// <see cref="SearchParameterInfo.Name"/> values of each component.
        /// The same collection of search parameters (irrespective of their order in the input)
        /// will return the same hash.
        /// </summary>
        /// <param name="searchParamInfo">A list of <see cref="SearchSearchParameterInfo" /></param>
        /// <returns>A hash based on the search parameter names present in the input.</returns>
        public static string CalculateSearchParameterNameHash(IEnumerable<SearchParameterInfo> searchParamInfo)
        {
            EnsureArg.IsNotNull(searchParamInfo, nameof(searchParamInfo));
            EnsureArg.IsGt(searchParamInfo.Count(), 0, nameof(searchParamInfo));

            StringBuilder sb = new StringBuilder();
            foreach (string paramName in searchParamInfo.Select(x => x.Name).OrderBy(x => x))
            {
                sb.Append(paramName);
            }

            string hash = sb.ToString().ComputeHash();
            return hash;
        }
    }
}
