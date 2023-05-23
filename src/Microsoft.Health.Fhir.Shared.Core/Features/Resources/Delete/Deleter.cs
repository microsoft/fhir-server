// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class Deleter : IDeleter
    {
        private IResourceWrapperFactory _resourceWrapperFactory;
        private Lazy<IConformanceProvider> _conformanceProvider;
        private IFhirDataStore _fhirDataStore;
        private ISearchService _searchService;
        private ResourceIdProvider _resourceIdProvider;

        public Deleter(
            IResourceWrapperFactory resourceWrapperFactory,
            Lazy<IConformanceProvider> conformanceProvider,
            IFhirDataStore fhirDataStore,
            ISearchService searchService,
            ResourceIdProvider resourceIdProvider)
        {
            _resourceWrapperFactory = resourceWrapperFactory;
            _conformanceProvider = conformanceProvider;
            _fhirDataStore = fhirDataStore;
            _searchService = searchService;
            _resourceIdProvider = resourceIdProvider;
        }

        public async Task<ResourceKey> DeleteAsync(DeleteResourceRequest request, CancellationToken cancellationToken)
        {
            var key = request.ResourceKey;

            if (!string.IsNullOrEmpty(key.VersionId))
            {
                throw new MethodNotAllowedException(Core.Resources.DeleteVersionNotAllowed);
            }

            string version = null;

            switch (request.DeleteOperation)
            {
                case DeleteOperation.SoftDelete:
                    var emptyInstance = (Resource)Activator.CreateInstance(ModelInfo.GetTypeForFhirType(request.ResourceKey.ResourceType));
                    emptyInstance.Id = request.ResourceKey.Id;

                    ResourceWrapper deletedWrapper = _resourceWrapperFactory.CreateResourceWrapper(emptyInstance, _resourceIdProvider, deleted: true, keepMeta: false);

                    bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(key.ResourceType, cancellationToken);

                    UpsertOutcome result = await _fhirDataStore.UpsertAsync(new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false), cancellationToken);

                    version = result?.Wrapper.Version;
                    break;
                case DeleteOperation.HardDelete:
                case DeleteOperation.PurgeHistory:
                    await _fhirDataStore.HardDeleteAsync(key, request.DeleteOperation == DeleteOperation.PurgeHistory, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            return new ResourceKey(key.ResourceType, key.Id, version);
        }

        public async Task<int> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            (IReadOnlyCollection<SearchResultEntry> matchedResults, string ct) = await _searchService.ConditionalSearchAsync(request.ResourceType, request.ConditionalParameters, request.MaxDeleteCount, cancellationToken);

            int itemsDeleted = 0;
            bool deleteAll = request.MaxDeleteCount < 0;

            // Delete the matched results...
            while (matchedResults.Any() || !string.IsNullOrEmpty(ct))
            {
                var resultsToDelete = deleteAll ? matchedResults : matchedResults.Take(request.MaxDeleteCount - itemsDeleted);
                foreach (SearchResultEntry item in resultsToDelete)
                {
                    var result = await DeleteAsync(new DeleteResourceRequest(request.ResourceType, item.Resource.ResourceId, request.DeleteOperation), cancellationToken);
                    itemsDeleted++;
                }

                if (!string.IsNullOrEmpty(ct) && (request.MaxDeleteCount - itemsDeleted > 0 || deleteAll))
                {
                    (matchedResults, ct) = await _searchService.ConditionalSearchAsync(
                        request.ResourceType,
                        request.ConditionalParameters,
                        deleteAll ? -1 : request.MaxDeleteCount - itemsDeleted,
                        cancellationToken,
                        ct);
                }
                else
                {
                    break;
                }
            }

            return itemsDeleted;
        }
    }
}
