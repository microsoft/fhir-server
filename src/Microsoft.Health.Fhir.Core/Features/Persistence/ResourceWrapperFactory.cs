// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
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
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly ICompartmentIndexer _compartmentIndexer;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IResourceDeserializer _resourceDeserializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceWrapperFactory"/> class.
        /// </summary>
        /// <param name="rawResourceFactory">The raw resource factory.</param>
        /// <param name="fhirRequestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="searchIndexer">The search indexer used to generate search indices.</param>
        /// <param name="claimsExtractor">The claims extractor used to extract claims.</param>
        /// <param name="compartmentIndexer">The compartment indexer.</param>
        /// <param name="searchParameterDefinitionManager"> The search parameter definition manager.</param>
        /// <param name="resourceDeserializer">Resource deserializer</param>
        /// <param name="resourceIdProvider">Resource id provider</param>
        public ResourceWrapperFactory(
            IRawResourceFactory rawResourceFactory,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            ISearchIndexer searchIndexer,
            IClaimsExtractor claimsExtractor,
            ICompartmentIndexer compartmentIndexer,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IResourceDeserializer resourceDeserializer,
            ResourceIdProvider resourceIdProvider)
        {
            EnsureArg.IsNotNull(rawResourceFactory, nameof(rawResourceFactory));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(compartmentIndexer, nameof(compartmentIndexer));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));

            _rawResourceFactory = rawResourceFactory;
            _searchIndexer = searchIndexer;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsExtractor = claimsExtractor;
            _compartmentIndexer = compartmentIndexer;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _resourceDeserializer = resourceDeserializer;
            ResourceIdProvider = resourceIdProvider;
        }

        public ResourceIdProvider ResourceIdProvider { get; }

        /// <inheritdoc />
        public ResourceWrapper Create(ResourceElement resource, bool deleted, bool keepMeta)
        {
            RawResource rawResource = _rawResourceFactory.Create(resource, keepMeta);
            IReadOnlyCollection<SearchIndexEntry> searchIndices = _searchIndexer.Extract(resource);

            string searchParamHash = _searchParameterDefinitionManager.GetSearchParameterHashForResourceType(resource.InstanceType);

            ExtractMinAndMaxValues(searchIndices);

            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

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

        /// <inheritdoc />
        public void Update(ResourceWrapper resourceWrapper)
        {
            var resourceElement = _resourceDeserializer.Deserialize(resourceWrapper);
            var newIndices = _searchIndexer.Extract(resourceElement);
            ExtractMinAndMaxValues(newIndices);
            resourceWrapper.UpdateSearchIndices(newIndices);
        }

        // A given search parameter can have multiple values. We want to keep track of which
        // of these values are the min and max for each parameter and mark the corresponding
        // SearchValue object appropriately.
        private static void ExtractMinAndMaxValues(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            var minValues = new Dictionary<Uri, ISupportSortSearchValue>();
            var maxValues = new Dictionary<Uri, ISupportSortSearchValue>();

            foreach (SearchIndexEntry currentEntry in searchIndices)
            {
                var currentValue = currentEntry.Value as ISupportSortSearchValue;
                if (currentValue == null)
                {
                    continue;
                }

                if (currentEntry.SearchParameter.SortStatus == SortParameterStatus.Disabled)
                {
                    continue;
                }

                if (minValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMinValue))
                {
                    if (currentValue.CompareTo(existingMinValue, ComparisonRange.Min) < 0)
                    {
                        minValues[currentEntry.SearchParameter.Url] = currentValue;
                    }
                }
                else
                {
                    minValues.Add(currentEntry.SearchParameter.Url, currentValue);
                }

                if (maxValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMaxValue))
                {
                    if (currentValue.CompareTo(existingMaxValue, ComparisonRange.Max) > 0)
                    {
                        maxValues[currentEntry.SearchParameter.Url] = currentValue;
                    }
                }
                else
                {
                    maxValues.Add(currentEntry.SearchParameter.Url, currentValue);
                }
            }

            foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in minValues)
            {
                kvp.Value.IsMin = true;
            }

            foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in maxValues)
            {
                kvp.Value.IsMax = true;
            }
        }
    }
}
