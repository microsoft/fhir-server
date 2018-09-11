// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Provides information about a search parameter.
    /// </summary>
    internal interface ISearchParam
    {
        /// <summary>
        /// Gets the resource type which supports this search parameter.
        /// </summary>
        Type ResourceType { get; }

        /// <summary>
        /// Gets the search parameter name.
        /// </summary>
        string ParamName { get; }

        /// <summary>
        /// Extracts values from a given <paramref name="resource"/>.
        /// </summary>
        /// <param name="resource">The resource whose values should be extracted.</param>
        /// <returns>An <see cref="IEnumerable{ISearchValue}"/> that contains the searchable values.</returns>
        IEnumerable<ISearchValue> ExtractValues(Resource resource);

        /// <summary>
        /// Parses the searchable value from <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The value to be parsed.</param>
        /// <returns>An instance of <see cref="ISearchValue"/> representing the searchable value.</returns>
        ISearchValue Parse(string value);

        /// <summary>
        /// Adds the values extractor, which is used to extract values out of the resource.
        /// </summary>
        /// <param name="extractor">The values extractor.</param>
        void AddExtractor(ISearchValuesExtractor extractor);
    }
}
