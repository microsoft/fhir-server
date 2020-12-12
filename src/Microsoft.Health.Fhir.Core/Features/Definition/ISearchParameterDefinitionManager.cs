﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public interface ISearchParameterDefinitionManager
    {
        public delegate ISearchParameterDefinitionManager SearchableSearchParameterDefinitionManagerResolver();

        public delegate ISearchParameterDefinitionManager SupportedSearchParameterDefinitionManagerResolver();

        /// <summary>
        /// Gets the list of all search parameters.
        /// </summary>
        IEnumerable<SearchParameterInfo> AllSearchParameters { get; }

        /// <summary>
        /// Represents a mapping of resource type to a hash of the search parameters
        /// currently supported for that resource type.
        /// </summary>
        IReadOnlyDictionary<string, string> SearchParameterHashMap { get; }

        /// <summary>
        /// Gets list of search parameters for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type whose list of search parameters should be returned.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the search parameters.</returns>
        IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="name"/> associated with <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="name">The name of the search parameter.</param>
        /// <param name="searchParameter">When this method returns, the search parameter with the given <paramref name="name"/> associated with the <paramref name="resourceType"/> if it exists; otherwise, the default value.</param>
        /// <returns><c>true</c> if the search parameter exists; otherwise, <c>false</c>.</returns>
        bool TryGetSearchParameter(string resourceType, string name, out SearchParameterInfo searchParameter);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="name"/> associated with <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="name">The name of the search parameter.</param>
        /// <returns>The search parameter with the given <paramref name="name"/> associated with the <paramref name="resourceType"/>.</returns>
        SearchParameterInfo GetSearchParameter(string resourceType, string name);

        /// <summary>
        /// Retrieves the search parameter with <paramref name="definitionUri"/>.
        /// </summary>
        /// <param name="definitionUri">The search parameter definition URL.</param>
        /// <returns>The search parameter with the given <paramref name="definitionUri"/>.</returns>
        SearchParameterInfo GetSearchParameter(Uri definitionUri);

        /// <summary>
        /// Updates the existing resource type - search parameter hash mapping with the given new values.
        /// </summary>
        /// <param name="updatedSearchParamHashMap">Dictionary containing resource type to search parameter hash values</param>
        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap);

        /// <summary>
        /// Gets the hash of the current search parameters that are supported for the given resource type.
        /// </summary>
        /// <param name="resourceType">Resource type for which we need the hash of search parameters.</param>
        /// <returns>A string representing a hash of the search parameters.</returns>
        public string GetSearchParameterHashForResourceType(string resourceType);
    }
}
