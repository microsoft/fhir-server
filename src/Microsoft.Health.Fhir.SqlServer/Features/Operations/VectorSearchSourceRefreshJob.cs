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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    [JobTypeId((int)JobType.VectorSearchSourceRefresh)]
    public class VectorSearchSourceRefreshJob : IJob
    {
        private readonly IVectorSearchSourceDependencyStore _dependencyStore;
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly IVectorSearchIndexer _vectorSearchIndexer;
        private readonly ILogger<VectorSearchSourceRefreshJob> _logger;

        public VectorSearchSourceRefreshJob(
            IVectorSearchSourceDependencyStore dependencyStore,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            IResourceWrapperFactory resourceWrapperFactory,
            ILogger<VectorSearchSourceRefreshJob> logger,
            IVectorSearchIndexer vectorSearchIndexer = null)
        {
            _dependencyStore = EnsureArg.IsNotNull(dependencyStore, nameof(dependencyStore));
            _fhirDataStoreFactory = EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            _resourceWrapperFactory = EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _vectorSearchIndexer = vectorSearchIndexer;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            VectorSearchSourceRefreshJobDefinition definition = JsonConvert.DeserializeObject<VectorSearchSourceRefreshJobDefinition>(jobInfo.Definition)
                ?? throw new InvalidOperationException("The vector search source refresh job definition is invalid.");

            if (_vectorSearchIndexer == null)
            {
                _logger.LogInformation("Skipping vector search source refresh job {JobId} because vector search indexing is disabled.", jobInfo.Id);
                return CreateResult(0, 0);
            }

            IReadOnlyCollection<ResourceKey> dependentKeys = await _dependencyStore.GetDependentResourceKeysAsync(
                definition.SourceResourceType,
                definition.SourceResourceId,
                cancellationToken);

            if (dependentKeys.Count == 0)
            {
                return CreateResult(0, 0);
            }

            using IScoped<IFhirDataStore> store = _fhirDataStoreFactory();
            IReadOnlyList<ResourceWrapper> resources = await store.Value.GetAsync(dependentKeys.ToList(), cancellationToken);

            if (resources.Count == 0)
            {
                return CreateResult(dependentKeys.Count, 0);
            }

            foreach (ResourceWrapper resource in resources)
            {
                _resourceWrapperFactory.Update(resource);
            }

            await _vectorSearchIndexer.IndexAsync(resources, cancellationToken);

            try
            {
                await store.Value.BulkUpdateSearchParameterIndicesAsync(resources, cancellationToken);
            }
            catch (PreconditionFailedException exception)
            {
                throw new JobExecutionSoftFailureException(
                    "A dependent resource changed while its vector search index was being refreshed.",
                    exception,
                    isCustomerCaused: false);
            }

            return CreateResult(dependentKeys.Count, resources.Count);
        }

        private static string CreateResult(int dependentResourceCount, int refreshedResourceCount)
        {
            return JsonConvert.SerializeObject(new Dictionary<string, int>
            {
                ["dependentResourceCount"] = dependentResourceCount,
                ["refreshedResourceCount"] = refreshedResourceCount,
            });
        }
    }
}
