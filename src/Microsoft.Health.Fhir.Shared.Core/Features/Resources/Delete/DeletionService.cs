// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class DeletionService : IDeletionService
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Lazy<IConformanceProvider> _conformanceProvider;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchService _searchService;
        private readonly ResourceIdProvider _resourceIdProvider;

        public DeletionService(
            IResourceWrapperFactory resourceWrapperFactory,
            Lazy<IConformanceProvider> conformanceProvider,
            IFhirDataStore fhirDataStore,
            ISearchService searchService,
            ResourceIdProvider resourceIdProvider)
        {
            _resourceWrapperFactory = EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            _conformanceProvider = EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            _fhirDataStore = EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _resourceIdProvider = EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
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
                    var emptyInstance = (Resource)Activator.CreateInstance(ModelInfo.GetTypeForFhirType(key.ResourceType));
                    emptyInstance.Id = request.ResourceKey.Id;

                    ResourceWrapper deletedWrapper = _resourceWrapperFactory.CreateResourceWrapper(emptyInstance, _resourceIdProvider, deleted: true, keepMeta: false);

                    bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(key.ResourceType, cancellationToken);

                    UpsertOutcome result = await _fhirDataStore.UpsertAsync(new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleOperationId: null), cancellationToken);

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

        public async Task<IReadOnlySet<string>> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken)
        {
            (IReadOnlyCollection<SearchResultEntry> matchedResults, string ct) = await _searchService.ConditionalSearchAsync(request.ResourceType, request.ConditionalParameters, cancellationToken, request.MaxDeleteCount);

            var itemsDeleted = new HashSet<string>();

            // Delete the matched results...
            try
            {
                while (matchedResults.Any() || !string.IsNullOrEmpty(ct))
                {
                    IReadOnlyCollection<SearchResultEntry> resultsToDelete =
                        request.DeleteAll ? matchedResults : matchedResults.Take(request.MaxDeleteCount.Value - itemsDeleted.Count)
                            .ToArray();

                    if (request.DeleteOperation == DeleteOperation.SoftDelete)
                    {
                        bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(request.ResourceType, cancellationToken);

                        try
                        {
                            await _fhirDataStore.MergeAsync(
                                resultsToDelete.Select(item =>
                                {
                                    var emptyInstance = (Resource)Activator.CreateInstance(ModelInfo.GetTypeForFhirType(request.ResourceType));
                                    emptyInstance.Id = item.Resource.ResourceId;
                                    ResourceWrapper deletedWrapper = _resourceWrapperFactory.CreateResourceWrapper(emptyInstance, _resourceIdProvider, deleted: true, keepMeta: false);
                                    return new ResourceWrapperOperation(deletedWrapper, true, keepHistory, null, false, false, bundleOperationId: null);
                                }).ToArray(),
                                cancellationToken);
                        }
                        catch (PartialSuccessException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> ex)
                        {
                            foreach (string id in ex.PartialResults.Select(item => item.Key.Id))
                            {
                                itemsDeleted.Add(id);
                            }

                            throw;
                        }

                        foreach (string id in itemsDeleted.Concat(resultsToDelete.Select(item => item.Resource.ResourceId)))
                        {
                            itemsDeleted.Add(id);
                        }
                    }
                    else
                    {
                        var options = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = 4,
                            CancellationToken = cancellationToken,
                        };

                        var parallelBag = new ConcurrentBag<string>();
                        try
                        {
                            await Parallel.ForEachAsync(resultsToDelete, options, async (item, ct) =>
                            {
                                parallelBag.Add((await DeleteAsync(new DeleteResourceRequest(request.ResourceType, item.Resource.ResourceId, request.DeleteOperation), ct)).Id);
                            });
                        }
                        finally
                        {
                            foreach (string item in parallelBag)
                            {
                                itemsDeleted.Add(item);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(ct) && (request.MaxDeleteCount - itemsDeleted.Count > 0 || request.DeleteAll))
                    {
                        (matchedResults, ct) = await _searchService.ConditionalSearchAsync(
                            request.ResourceType,
                            request.ConditionalParameters,
                            cancellationToken,
                            request.DeleteAll ? null : request.MaxDeleteCount - itemsDeleted.Count,
                            ct);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new PartialSuccessException<IReadOnlySet<string>>(ex, itemsDeleted);
            }

            return itemsDeleted;
        }
    }
}
