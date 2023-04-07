// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    public static class SearchParameterDefinitionManagerExtensions
    {
        /// <summary>
        /// Retrieves the search parameters that match the given <paramref name="resourceTypes"/>.
        /// </summary>
        /// <param name="manager">The search parameter definition manager.</param>
        /// <param name="resourceTypes">Collection of resource type names</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        public static IEnumerable<SearchParameterInfo> GetSearchParametersByResourceTypes(this SearchParameterDefinitionManager manager, ICollection<string> resourceTypes)
        {
            return manager.TypeLookup.Where(t => resourceTypes.Contains(t.Key)).SelectMany(t => t.Value.Values);
        }

        /// <summary>
        /// Retrieves the search parameters that match the given <paramref name="definitionUrls"/>.
        /// </summary>
        /// <param name="manager">The search parameter definition manager.</param>
        /// <param name="definitionUrls">Collection of definition urls</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        public static IEnumerable<SearchParameterInfo> GetSearchParametersByUrls(this SearchParameterDefinitionManager manager, ICollection<string> definitionUrls)
        {
            return manager.UrlLookup.Where(t => definitionUrls.Contains(t.Key)).Select(t => t.Value);
        }

        /// <summary>
        /// Retrieves the search parameters that match the given <paramref name="codes"/>.
        /// </summary>
        /// <param name="manager">The search parameter definition manager.</param>
        /// <param name="codes">Collection of codes</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        public static IEnumerable<SearchParameterInfo> GetSearchParametersByCodes(this SearchParameterDefinitionManager manager, ICollection<string> codes)
        {
            return manager.UrlLookup.Where(t => codes.Contains(t.Value.Code)).Select(t => t.Value);
        }
    }
}
