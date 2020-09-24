// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        /// <see cref="ResourceSearchParameterStatus.Uri"/> values of each component. The same collection
        /// of search parameter status (irrespective of their order in the input) will return the same hash.
        /// </summary>
        /// <param name="resourceSearchParameterStatus">A list of <see cref="ResourceSearchParameterStatus" /></param>
        /// <returns>A hash based on the search parameter uri.</returns>
        public static string CalculateSearchParameterHash(IEnumerable<ResourceSearchParameterStatus> resourceSearchParameterStatus)
        {
            EnsureArg.IsNotNull(resourceSearchParameterStatus, nameof(resourceSearchParameterStatus));
            EnsureArg.IsGt(resourceSearchParameterStatus.Count(), 0, nameof(resourceSearchParameterStatus));

            return CalculateSearchParameterHash(resourceSearchParameterStatus.Select(x => x.Uri));
        }

        /// <summary>
        /// Calculates a hash given a list of <see cref="Uri" /> representing search parameters.
        /// The same collection of uris (irrespective of their order in the input) will return the same hash.
        /// </summary>
        /// <param name="searchParameterUris">A list of <see cref="Uri" /></param>
        /// <returns>A hash based on the search parameter uri.</returns>
        public static string CalculateSearchParameterHash(IEnumerable<Uri> searchParameterUris)
        {
            EnsureArg.IsNotNull(searchParameterUris, nameof(searchParameterUris));
            EnsureArg.IsGt(searchParameterUris.Count(), 0, nameof(searchParameterUris));

            StringBuilder sb = new StringBuilder();
            foreach (Uri uri in searchParameterUris.OrderBy(x => x.ToString()))
            {
                sb.Append(uri.ToString());
            }

            string hash = sb.ToString().ComputeHash();
            return hash;
        }
    }
}
