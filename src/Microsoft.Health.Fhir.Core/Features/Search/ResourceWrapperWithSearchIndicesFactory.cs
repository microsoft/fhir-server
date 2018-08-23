// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="ResourceWrapper"/>.
    /// </summary>
    public class ResourceWrapperWithSearchIndicesFactory : IResourceWrapperFactory
    {
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly ISearchIndexer _searchIndexer;
        private readonly IFhirContextAccessor _fhirContextAccessor;
        private readonly IClaimsIndexer _claimsIndexer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceWrapperWithSearchIndicesFactory"/> class.
        /// </summary>
        /// <param name="rawResourceFactory">The raw resource factory.</param>
        /// <param name="fhirContextAccessor">Fhir context accessor.</param>
        /// <param name="searchIndexer">The search indexer used to generate search indices.</param>
        /// <param name="claimsIndexer">The claims indexer used to generate claims indices.</param>
        public ResourceWrapperWithSearchIndicesFactory(IRawResourceFactory rawResourceFactory, IFhirContextAccessor fhirContextAccessor, ISearchIndexer searchIndexer, IClaimsIndexer claimsIndexer)
        {
            EnsureArg.IsNotNull(rawResourceFactory, nameof(rawResourceFactory));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));
            EnsureArg.IsNotNull(claimsIndexer, nameof(claimsIndexer));

            _rawResourceFactory = rawResourceFactory;
            _searchIndexer = searchIndexer;
            _fhirContextAccessor = fhirContextAccessor;
            _claimsIndexer = claimsIndexer;
        }

        /// <inheritdoc />
        public ResourceWrapper Create(Resource resource, bool deleted)
        {
            RawResource rawResource = _rawResourceFactory.Create(resource);
            IReadOnlyCollection<SearchIndexEntry> searchIndices = _searchIndexer.Extract(resource);

            return new ResourceWrapperWithSearchIndices(
                resource,
                rawResource,
                new ResourceRequest(_fhirContextAccessor.FhirContext.RequestUri, _fhirContextAccessor.FhirContext.HttpMethod?.ToString()),
                deleted,
                searchIndices,
                _claimsIndexer.Extract());
        }
    }
}
