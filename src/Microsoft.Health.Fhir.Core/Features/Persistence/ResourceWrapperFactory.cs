// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="ResourceWrapper"/>.
    /// </summary>
    public class ResourceWrapperFactory : IResourceWrapperFactory
    {
        /// <summary>
        /// Request context property that holds the number of search index values extracted for a bundle entry's resource.
        /// </summary>
        public const string SearchIndexValuesPropertyName = "Bundle.Entry.SearchIndexValues";

        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly ISearchIndexer _searchIndexer;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly ICompartmentIndexer _compartmentIndexer;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly IResourceIdentityValidator _resourceIdentityValidator;
        private readonly IReferenceStorageValidator _referenceStorageValidator;
        private readonly IVectorSearchIndexer _vectorSearchIndexer;

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
        /// <param name="resourceIdentityValidator">Validates the resource identities admitted as new content.</param>
        /// <param name="referenceStorageValidator">Checks that the data store can hold each extracted reference whole.</param>
        /// <param name="vectorSearchIndexer">The optional vector indexer, registered when vector search is enabled.</param>
        public ResourceWrapperFactory(
            IRawResourceFactory rawResourceFactory,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            ISearchIndexer searchIndexer,
            IClaimsExtractor claimsExtractor,
            ICompartmentIndexer compartmentIndexer,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IResourceDeserializer resourceDeserializer,
            IResourceIdentityValidator resourceIdentityValidator,
            IReferenceStorageValidator referenceStorageValidator,
            IVectorSearchIndexer vectorSearchIndexer = null)
        {
            EnsureArg.IsNotNull(rawResourceFactory, nameof(rawResourceFactory));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(compartmentIndexer, nameof(compartmentIndexer));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(resourceIdentityValidator, nameof(resourceIdentityValidator));
            EnsureArg.IsNotNull(referenceStorageValidator, nameof(referenceStorageValidator));

            _rawResourceFactory = rawResourceFactory;
            _searchIndexer = searchIndexer;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _claimsExtractor = claimsExtractor;
            _compartmentIndexer = compartmentIndexer;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _resourceDeserializer = resourceDeserializer;
            _resourceIdentityValidator = resourceIdentityValidator;
            _referenceStorageValidator = referenceStorageValidator;
            _vectorSearchIndexer = vectorSearchIndexer;
        }

        /// <inheritdoc />
        public ResourceWrapper Create(ResourceElement resource, bool deleted, bool keepMeta, bool keepVersion = false)
        {
            if (!deleted)
            {
                IReadOnlyList<ValidationFailure> identityFailures = _resourceIdentityValidator.Validate(resource);
                if (identityFailures.Count != 0)
                {
                    throw new ResourceNotValidException(identityFailures);
                }
            }

            RawResource rawResource = _rawResourceFactory.Create(resource, keepMeta, keepVersion);
            IReadOnlyCollection<SearchIndexEntry> searchIndices = ExtractAdmittedSearchIndices(deleted ? CreateTombstoneIndexSource(resource) : resource);

            if (!deleted)
            {
                EnsureReferencesStorable(resource, searchIndices);
            }

            if (deleted && searchIndices.Any(x => x.SearchParameter.Code != KnownQueryParameterNames.LastUpdated && x.SearchParameter.Code != KnownQueryParameterNames.Id))
            {
                searchIndices = searchIndices.Where(x => x.SearchParameter.Code == KnownQueryParameterNames.LastUpdated || x.SearchParameter.Code == KnownQueryParameterNames.Id).ToList();
            }

            string searchParamHash = _searchParameterDefinitionManager.GetSearchParameterHashForResourceType(resource.InstanceType);

            ExtractMinAndMaxValues(searchIndices);

            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

            if (!deleted && fhirRequestContext.ExecutingBatchOrTransaction)
            {
                fhirRequestContext.Properties[SearchIndexValuesPropertyName] = searchIndices.Count;
            }

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
            StageSearchIndices(resourceWrapper).Apply();
        }

        /// <inheritdoc />
        public async Task UpdateAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));

            // Staging every replacement before applying any keeps an extraction failure from changing part of the batch's indices.
            var staged = new List<StagedSearchIndices>(resources.Count);
            foreach (ResourceWrapper resource in resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                staged.Add(StageSearchIndices(resource));
            }

            foreach (StagedSearchIndices replacement in staged)
            {
                replacement.Apply();
            }

            // Update already extracted every index from the persisted resource, so vector sources need no refresh.
            await IndexVectorsAsync(resources, cancellationToken);
        }

        /// <inheritdoc />
        public async Task UpdateVectorSearchIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));
            cancellationToken.ThrowIfCancellationRequested();

            if (_vectorSearchIndexer == null)
            {
                return;
            }

            foreach (ResourceWrapper resource in resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RefreshVectorSources(resource);
            }

            await IndexVectorsAsync(resources, cancellationToken);
        }

        // A tombstone keeps only _id and _lastUpdated, so legacy content it discards must never be parsed.
        private static ResourceElement CreateTombstoneIndexSource(ResourceElement resource)
        {
            ElementNode root = ElementNode.FromElement(resource.Instance);
            RemoveChildrenExcept(root, "id", "meta");

            foreach (ElementNode meta in root.Children("meta").Cast<ElementNode>())
            {
                RemoveChildrenExcept(meta, "lastUpdated");
            }

            return new ResourceElement(root);
        }

        private static void RemoveChildrenExcept(ElementNode node, params string[] retainedNames)
        {
            foreach (ElementNode child in node.Children().Cast<ElementNode>().Where(c => !retainedNames.Contains(c.Name, StringComparer.Ordinal)).ToList())
            {
                node.Remove(child);
            }
        }

        private IReadOnlyCollection<SearchIndexEntry> ExtractAdmittedSearchIndices(ResourceElement resource)
        {
            try
            {
                return _searchIndexer.Extract(resource);
            }
            catch (ResourceIdentityFormatException ex)
            {
                throw new ResourceNotValidException(new[] { new ValidationFailure(resource.InstanceType, ex.Message) });
            }
        }

        private void EnsureReferencesStorable(ResourceElement resource, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            try
            {
                foreach (SearchIndexEntry entry in searchIndices)
                {
                    EnsureStorable(entry.Value);
                }
            }
            catch (ReferenceStorageCapacityException ex)
            {
                throw new ResourceNotValidException(new[] { new ValidationFailure(resource.InstanceType, ex.Message) });
            }
        }

        private void EnsureStorable(ISearchValue value)
        {
            switch (value)
            {
                case ReferenceSearchValue reference:
                    _referenceStorageValidator.EnsureRepresentable(reference);
                    break;
                case CompositeSearchValue composite:
                    foreach (ISearchValue componentValue in composite.Components.SelectMany(component => component))
                    {
                        EnsureStorable(componentValue);
                    }

                    break;
            }
        }

        private StagedSearchIndices StageSearchIndices(ResourceWrapper resourceWrapper)
        {
            if (resourceWrapper.IsDeleted)
            {
                var retainedIndices = resourceWrapper.SearchIndices.Where(x => x.SearchParameter.Code == KnownQueryParameterNames.LastUpdated || x.SearchParameter.Code == KnownQueryParameterNames.Id).ToList();
                return new StagedSearchIndices(resourceWrapper, retainedIndices, ReplacesHash: false, SearchParameterHash: null);
            }

            ResourceElement resourceElement = _resourceDeserializer.Deserialize(resourceWrapper);
            IReadOnlyCollection<SearchIndexEntry> newIndices = _searchIndexer.Extract(resourceElement);
            EnsureReferencesStorable(resourceElement, newIndices);
            string searchParameterHash = _searchParameterDefinitionManager.GetSearchParameterHashForResourceType(resourceElement.InstanceType);
            ExtractMinAndMaxValues(newIndices);
            return new StagedSearchIndices(resourceWrapper, newIndices, ReplacesHash: true, searchParameterHash);
        }

        private async Task IndexVectorsAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_vectorSearchIndexer != null && resources.Count > 0)
            {
                await _vectorSearchIndexer.UpdateVectorSearchIndicesAsync(resources, cancellationToken);
            }
        }

        // Vector sources are FHIRPath values, and an expression such as meta.versionId reads state that
        // only exists once the version is finalized. Create extracted them from the input resource, so
        // re-extract them from the finalized one; otherwise a reindex of the same resource would produce
        // different passages. Ordinary indices, the search parameter hash and min/max flags stay as created.
        private void RefreshVectorSources(ResourceWrapper resource)
        {
            if (resource.IsDeleted
                || resource.IsHistory
                || !_searchParameterDefinitionManager.GetSearchParameters(resource.ResourceTypeName).Any(searchParameter => searchParameter.VectorConfig != null))
            {
                return;
            }

            IEnumerable<SearchIndexEntry> vectorSources = _searchIndexer
                .Extract(_resourceDeserializer.Deserialize(resource))
                .Where(entry => entry.SearchParameter.VectorConfig != null);

            resource.UpdateSearchIndices(
                (resource.SearchIndices ?? Array.Empty<SearchIndexEntry>())
                .Where(entry => entry.SearchParameter.VectorConfig == null)
                .Concat(vectorSources)
                .ToList());
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

        private readonly record struct StagedSearchIndices(
            ResourceWrapper Wrapper,
            IReadOnlyCollection<SearchIndexEntry> SearchIndices,
            bool ReplacesHash,
            string SearchParameterHash)
        {
            public void Apply()
            {
                if (ReplacesHash)
                {
                    Wrapper.UpdateSearchIndices(SearchIndices, SearchParameterHash);
                }
                else
                {
                    Wrapper.UpdateSearchIndices(SearchIndices);
                }
            }
        }
    }
}
