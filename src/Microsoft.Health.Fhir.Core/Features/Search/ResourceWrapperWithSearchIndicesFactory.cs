// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
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
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IClaimsIndexer _claimsIndexer;
        private readonly ICompartmentIndexer _compartmentIndexer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceWrapperWithSearchIndicesFactory"/> class.
        /// </summary>
        /// <param name="rawResourceFactory">The raw resource factory.</param>
        /// <param name="fhirRequestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="searchIndexer">The search indexer used to generate search indices.</param>
        /// <param name="claimsIndexer">The claims indexer used to generate claims indices.</param>
        /// <param name="compartmentIndexer">The compartment indexer.</param>
        public ResourceWrapperWithSearchIndicesFactory(IRawResourceFactory rawResourceFactory, IFhirRequestContextAccessor fhirRequestContextAccessor, ISearchIndexer searchIndexer, IClaimsIndexer claimsIndexer, ICompartmentIndexer compartmentIndexer)
        {
            EnsureArg.IsNotNull(rawResourceFactory, nameof(rawResourceFactory));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsIndexer, nameof(claimsIndexer));
            EnsureArg.IsNotNull(compartmentIndexer, nameof(compartmentIndexer));

            _rawResourceFactory = rawResourceFactory;
            _searchIndexer = searchIndexer;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsIndexer = claimsIndexer;
            _compartmentIndexer = compartmentIndexer;
        }

        /// <inheritdoc />
        public ResourceWrapper Create(Resource resource, bool deleted)
        {
            RawResource rawResource = _rawResourceFactory.Create(resource);
            IReadOnlyCollection<SearchIndexEntry> searchIndices = _searchIndexer.Extract(resource);
            var compartmentIndices = new CompartmentIndices(_compartmentIndexer);
            compartmentIndices.Extract(resource.ResourceType, searchIndices);

            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            return new ResourceWrapperWithSearchIndices(
                resource,
                rawResource,
                new ResourceRequest(fhirRequestContext.Uri, fhirRequestContext.Method),
                deleted,
                searchIndices,
                compartmentIndices,
                _claimsIndexer.Extract());
        }
    }
}
