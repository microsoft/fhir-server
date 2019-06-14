// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public interface ISearchParameterDefinitionManager
    {
        /// <summary>
        /// Gets the list of all search parameters.
        /// </summary>
        IEnumerable<SearchParameterInfo> AllSearchParameters { get; }

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
        /// Gets the type of a search parameter expression. In the case of a composite search parameter, the component parameter
        /// can be specified, to retrieve the type of that component.
        /// </summary>
        /// <param name="searchParameter">The search parameter</param>
        /// <param name="componentIndex">The optional component index if the search parameter is a composite</param>
        /// <returns>The search parameter type.</returns>
        SearchParamType GetSearchParameterType(SearchParameterInfo searchParameter, int? componentIndex);
    }
}
