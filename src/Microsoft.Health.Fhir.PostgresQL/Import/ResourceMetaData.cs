// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.PostgresQL.Import
{
    internal class ResourceMetadata
    {
        private readonly ILookup<Type, SearchIndexEntry> _groupedSearchIndices;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        public ResourceMetadata(CompartmentIndices compartmentIndices = null, ILookup<Type, SearchIndexEntry> groupedSearchIndices = null, IReadOnlyCollection<KeyValuePair<string, string>> resourceWriteClaims = null)
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        {
            Compartments = compartmentIndices;
            _groupedSearchIndices = groupedSearchIndices;
            WriteClaims = resourceWriteClaims;
        }

        public CompartmentIndices Compartments { get; }

        public IReadOnlyCollection<KeyValuePair<string, string>> WriteClaims { get; }

        /// <summary>
        /// Gets the search index entries by their type key. The type should be one returned by
        /// <see cref="SearchParameterToSearchValueTypeMap.GetSearchValueType(Hl7.Fhir.Model.SearchParameter)"/>:
        /// either implementing ISearchValue or for composites a Tuple with the component types as type arguments,
        /// for example: <see cref="Tuple{UriSearchValue}"/>
        /// </summary>
        /// <param name="type">A type representing the search parameter type</param>
        /// <returns>The parameters corresponding to the given type.</returns>
        public IEnumerable<SearchIndexEntry> GetSearchIndexEntriesByType(Type type)
        {
            // TODO: ensure all types are consumed before disposal
            return _groupedSearchIndices == null ? Enumerable.Empty<SearchIndexEntry>() : _groupedSearchIndices[type];
        }
    }
}
