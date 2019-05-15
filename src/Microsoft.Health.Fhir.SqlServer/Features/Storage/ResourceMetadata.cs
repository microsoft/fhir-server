// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class ResourceMetadata
    {
        private readonly ILookup<Type, SearchIndexEntry> _groupedSearchIndices;

        public ResourceMetadata(CompartmentIndices compartmentIndices = null, ILookup<Type, SearchIndexEntry> groupedSearchIndices = null, IReadOnlyCollection<KeyValuePair<string, string>> resourceWriteClaims = null)
        {
            Compartments = compartmentIndices;
            _groupedSearchIndices = groupedSearchIndices;
            WriteClaims = resourceWriteClaims;
        }

        public CompartmentIndices Compartments { get; }

        public IReadOnlyCollection<KeyValuePair<string, string>> WriteClaims { get; }

        public IEnumerable<SearchIndexEntry> GetSearchIndexEntriesByType(Type type)
        {
            // TODO: ensure all types are consumed before disposal
            return _groupedSearchIndices == null ? Enumerable.Empty<SearchIndexEntry>() : _groupedSearchIndices[type];
        }
    }
}
