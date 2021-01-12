// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="ResourceWrapper"/>.
    /// </summary>
    public class ResourceWrapperFactory : IResourceWrapperFactory
    {
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly ISearchIndexer _searchIndexer;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly ICompartmentIndexer _compartmentIndexer;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceWrapperFactory"/> class.
        /// </summary>
        /// <param name="rawResourceFactory">The raw resource factory.</param>
        /// <param name="fhirRequestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="searchIndexer">The search indexer used to generate search indices.</param>
        /// <param name="claimsExtractor">The claims extractor used to extract claims.</param>
        /// <param name="compartmentIndexer">The compartment indexer.</param>
        /// <param name="searchParameterDefinitionManager"> The search parameter definition manager.</param>
        public ResourceWrapperFactory(
            IRawResourceFactory rawResourceFactory,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            ISearchIndexer searchIndexer,
            IClaimsExtractor claimsExtractor,
            ICompartmentIndexer compartmentIndexer,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(rawResourceFactory, nameof(rawResourceFactory));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(compartmentIndexer, nameof(compartmentIndexer));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _rawResourceFactory = rawResourceFactory;
            _searchIndexer = searchIndexer;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsExtractor = claimsExtractor;
            _compartmentIndexer = compartmentIndexer;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        /// <inheritdoc />
        public ResourceWrapper Create(ResourceElement resource, bool deleted, bool keepMeta)
        {
            RawResource rawResource = _rawResourceFactory.Create(resource, keepMeta);
            IReadOnlyCollection<SearchIndexEntry> searchIndices = _searchIndexer.Extract(resource);
            string searchParamHash = _searchParameterDefinitionManager.GetSearchParameterHashForResourceType(resource.InstanceType);

            ExtractMinAndMaxValues(searchIndices);

            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            return new ResourceWrapper(
                resource,
                rawResource,
                new ResourceRequest(fhirRequestContext.Method, fhirRequestContext.Uri),
                deleted,
                searchIndices,
                _compartmentIndexer.Extract(resource.InstanceType, searchIndices),
                _claimsExtractor.Extract(),
                searchParamHash);
        }

        // A given search parameter can have multiple values. We want to keep track of which
        // of these values are the min and max for each parameter and mark the corresponding
        // SearchValue object appropriately.
        private void ExtractMinAndMaxValues(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            Dictionary<Uri, ISearchValue> minValues = new Dictionary<Uri, ISearchValue>();
            Dictionary<Uri, ISearchValue> maxValues = new Dictionary<Uri, ISearchValue>();

            foreach (SearchIndexEntry currentEntry in searchIndices)
            {
                // Currently we are tracking the min/max values only for string type parameters.
                if (currentEntry.SearchParameter.Type != ValueSets.SearchParamType.String)
                {
                    continue;
                }

                if (minValues.TryGetValue(currentEntry.SearchParameter.Url, out ISearchValue existingMinValue))
                {
                    if (currentEntry.Value.Compare(existingMinValue) < 0)
                    {
                        minValues[currentEntry.SearchParameter.Url] = currentEntry.Value;
                    }
                }
                else
                {
                    minValues.Add(currentEntry.SearchParameter.Url, currentEntry.Value);
                }

                if (maxValues.TryGetValue(currentEntry.SearchParameter.Url, out ISearchValue existingMaxValue))
                {
                    if (currentEntry.Value.Compare(existingMaxValue) > 0)
                    {
                        maxValues[currentEntry.SearchParameter.Url] = currentEntry.Value;
                    }
                }
                else
                {
                    maxValues.Add(currentEntry.SearchParameter.Url, currentEntry.Value);
                }
            }

            // Update the actual StringSearchValue objects with the appropriate IsMin/IsMax value
            foreach (KeyValuePair<Uri, ISearchValue> kvp in minValues)
            {
                kvp.Value.IsMin = true;
            }

            foreach (KeyValuePair<Uri, ISearchValue> kvp in maxValues)
            {
                kvp.Value.IsMax = true;
            }
        }
    }
}
