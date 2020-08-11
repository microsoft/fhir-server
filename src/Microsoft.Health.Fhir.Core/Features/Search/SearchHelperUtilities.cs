// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Text;
using EnsureThat;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public static class SearchHelperUtilities
    {
        /// <summary>
        /// Given a list of <see cref="ResourceSearchParameterStatus"/> calculates a hash using the
        /// <see cref="ResourceSearchParameterStatus.Uri"/> and <see cref="ResourceSearchParameterStatus.LastUpdated"/>
        /// values of each component. The same collection of search parameter status (irrespective of their order in the input)
        /// will return the same hash.
        /// </summary>
        /// <param name="resourceSearchParameterStatus">A list of <see cref="ResourceSearchParameterStatus" /></param>
        /// <returns>A hash based on the search parameter uri and last updated value.</returns>
        public static string CalculateSearchParameterHash(IEnumerable<ResourceSearchParameterStatus> resourceSearchParameterStatus)
        {
            EnsureArg.IsNotNull(resourceSearchParameterStatus, nameof(resourceSearchParameterStatus));
            EnsureArg.IsGt(resourceSearchParameterStatus.Count(), 0, nameof(resourceSearchParameterStatus));

            StringBuilder sb = new StringBuilder();
            foreach (ResourceSearchParameterStatus searchParameterStatus in resourceSearchParameterStatus.OrderBy(x => x.Uri.ToString()))
            {
                sb.Append(searchParameterStatus.Uri.ToString());
                sb.Append(searchParameterStatus.LastUpdated.ToString());
            }

            string hash = sb.ToString().ComputeHash();
            return hash;
        }
    }
}
