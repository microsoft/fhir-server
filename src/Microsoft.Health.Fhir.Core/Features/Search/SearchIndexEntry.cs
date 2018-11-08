// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents a search index entry.
    /// </summary>
    public class SearchIndexEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchIndexEntry"/> class.
        /// </summary>
        /// <param name="paramName">The search parameter name.</param>
        /// <param name="value">The searchable value.</param>
        public SearchIndexEntry(string paramName, ISearchValue value)
        {
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));
            EnsureArg.IsNotNull(value, nameof(value));

            ParamName = paramName;
            Value = value;
        }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public string ParamName { get; }

        /// <summary>
        /// Gets the searchable value.
        /// </summary>
        public ISearchValue Value { get; }
    }
}
