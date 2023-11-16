// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    [JobTypeId((int)JobType.BulkDeleteOrchestrator)]
    public class BulkDeleteOrchestratorJob : IJob
    {
        private readonly IQueueClient _queueClient;
        private readonly ISearchService _searchService;
        private const string OperationCompleted = "Completed";

        public BulkDeleteOrchestratorJob(
            IQueueClient queueClient,
            ISearchService searchService)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _queueClient = queueClient;
            _searchService = searchService;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(progress, nameof(progress));

            BulkDeleteDefinition definition = jobInfo.DeserializeDefinition<BulkDeleteDefinition>();

            var definitions = new List<BulkDeleteDefinition>();

            var searchParameters = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>(KnownQueryParameterNames.Summary, "count"),
            };

            if (definition.SearchParameters != null)
            {
                searchParameters.AddRange(definition.SearchParameters);
            }

            if (string.IsNullOrEmpty(definition.Type))
            {
                IReadOnlyList<string> resourceTypes = await _searchService.GetUsedResourceTypes(cancellationToken);

                foreach (var resourceType in resourceTypes)
                {
                    int numResources = (await _searchService.SearchAsync(resourceType, searchParameters.AsReadOnly(), cancellationToken, resourceVersionTypes: definition.VersionType)).TotalCount.GetValueOrDefault();

                    var processingDefinition = new BulkDeleteDefinition(
                        JobType.BulkDeleteProcessing,
                        definition.DeleteOperation,
                        resourceType,
                        definition.SearchParameters,
                        definition.Url,
                        definition.BaseUrl,
                        definition.ParentRequestId,
                        numResources,
                        definition.VersionType);
                    definitions.Add(processingDefinition);
                }
            }
            else
            {
                int numResources = (await _searchService.SearchAsync(definition.Type, searchParameters.AsReadOnly(), cancellationToken)).TotalCount.GetValueOrDefault();

                var processingDefinition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, definition.DeleteOperation, definition.Type, definition.SearchParameters, definition.Url, definition.BaseUrl, definition.ParentRequestId, numResources);
                definitions.Add(processingDefinition);
            }

            await _queueClient.EnqueueAsync(QueueType.BulkDelete, cancellationToken, jobInfo.GroupId, definitions: definitions.ToArray());
            return OperationCompleted;
        }
    }
}
