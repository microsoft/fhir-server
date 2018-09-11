// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Defines mechanisms to support for creating <see cref="SearchParam"/> objects.
    /// </summary>
    public interface ISearchParamFactory
    {
        /// <summary>
        /// Creates a <see cref="SearchParam"/>
        /// </summary>
        /// <param name="resourceType">The resource type that contains the search paramter.</param>
        /// <param name="paramName">The search parameter name.</param>
        /// <param name="parser">The parser used to parse the string representation of the search parameter value to an instance of <see cref="ISearchValue"/>.</param>
        /// <returns>An instance of <see cref="SearchParam"/> representing the search parameter.</returns>
        SearchParam CreateSearchParam(
            Type resourceType,
            string paramName,
            SearchParamValueParser parser);

        /// <summary>
        /// Creates a <see cref="CompositeSearchParam"/>
        /// </summary>
        /// <param name="resourceType">The resource type that contains the search paramter.</param>
        /// <param name="paramName">The search parameter name.</param>
        /// <param name="underlyingSearchParamType">The underlying search parameter type of the composite search parameter.</param>
        /// <param name="parser">The parser used to parse the string representation of the search parameter value to an instance of <see cref="ISearchValue"/>.</param>
        /// <returns>An instance of <see cref="SearchParam"/> representing the search parameter.</returns>
        SearchParam CreateCompositeSearchParam(
            Type resourceType,
            string paramName,
            SearchParamType underlyingSearchParamType,
            SearchParamValueParser parser);
    }
}
