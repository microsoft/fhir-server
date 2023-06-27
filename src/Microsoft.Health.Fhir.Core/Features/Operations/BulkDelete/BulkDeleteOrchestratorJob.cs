// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Operations.BulkDelete
{
    [JobTypeId((int)JobType.BulkDeleteOrchestrator)]
    public class BulkDeleteOrchestratorJob : IJob
    {
        private IQueueClient _queueClient;
        private ISearchService _searchService;

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

            var definitions = new List<string>();
            if (string.IsNullOrEmpty(definition.Type))
            {
                var resourceTypes = await _searchService.GetUsedResourceTypes(cancellationToken);

                foreach (var resourceType in resourceTypes)
                {
                    var processingDefinition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, definition.DeleteOperation, resourceType, definition.SearchParameters, definition.Url, definition.BaseUrl);
                    definitions.Add(JsonConvert.SerializeObject(processingDefinition));
                }
            }
            else
            {
                var processingDefinition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, definition.DeleteOperation, definition.Type, definition.SearchParameters, definition.Url, definition.BaseUrl);
                definitions.Add(JsonConvert.SerializeObject(processingDefinition));
            }

            await _queueClient.EnqueueAsync(QueueType.BulkDelete, cancellationToken, jobInfo.GroupId, definitions: definitions.ToArray());
            return "Completed";
        }
    }
}
