// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public interface ISearchParameterDefinitionManager
    {
        IEnumerable<SearchParameter> AllSearchParameters { get; }

        /// <summary>
        /// Gets list of search parameters for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type whose list of search parameters should be returned.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        IEnumerable<SearchParameter> GetSearchParameters(ResourceType resourceType);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="name"/> associated with <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="name">The name of the search parameter.</param>
        /// <param name="searchParameter">When this method returns, the search parameter with the given <paramref name="name"/> associated with the <paramref name="resourceType"/> if it exists; otherwise, the default value.</param>
        /// <returns><c>true</c> if the search parameter exists; otherwise, <c>false</c>.</returns>
        bool TryGetSearchParameter(ResourceType resourceType, string name, out SearchParameter searchParameter);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="name"/> associated with <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="name">The name of the search parameter.</param>
        /// <returns>The search parameter with the given <paramref name="name"/> associated with the <paramref name="resourceType"/>.</returns>
        SearchParameter GetSearchParameter(ResourceType resourceType, string name);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="definitionUri"/>.
        /// </summary>
        /// <param name="definitionUri">The search parameter definition URL.</param>
        /// <returns>The search parameter with the given <paramref name="definitionUri"/>.</returns>
        SearchParameter GetSearchParameter(Uri definitionUri);
    }
}
