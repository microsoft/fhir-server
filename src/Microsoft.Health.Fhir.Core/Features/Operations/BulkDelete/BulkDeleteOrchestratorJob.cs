// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

            BulkDeleteDefinition processingDefinition = null;

            if (string.IsNullOrEmpty(definition.Type))
            {
                IReadOnlyList<string> resourceTypes = await _searchService.GetUsedResourceTypes(cancellationToken);

                processingDefinition = await CreateProcessingDefinition(definition, _searchService, new List<string>(resourceTypes), cancellationToken);
            }
            else
            {
                processingDefinition = await CreateProcessingDefinition(definition, _searchService, new List<string>() { definition.Type }, cancellationToken);
            }

            await _queueClient.EnqueueAsync(QueueType.BulkDelete, cancellationToken, jobInfo.GroupId, definitions: processingDefinition);
            return OperationCompleted;
        }

        internal static async Task<BulkDeleteDefinition> CreateProcessingDefinition(BulkDeleteDefinition baseDefinition, ISearchService searchService, IList<string> resourceTypes, CancellationToken cancellationToken)
        {
            var searchParameters = new List<Tuple<string, string>>()
                {
                    new Tuple<string, string>(KnownQueryParameterNames.Summary, "count"),
                };

            if (baseDefinition.SearchParameters != null)
            {
                searchParameters.AddRange(baseDefinition.SearchParameters);
            }

            while (resourceTypes.Count > 0)
            {
                int numResources = (await searchService.SearchAsync(resourceTypes[0], searchParameters, cancellationToken, resourceVersionTypes: baseDefinition.VersionType)).TotalCount.GetValueOrDefault();

                if (numResources == 0)
                {
                    resourceTypes.RemoveAt(0);
                    continue;
                }

                string resourceType = resourceTypes.JoinByOrSeparator();

                return new BulkDeleteDefinition(
                    JobType.BulkDeleteProcessing,
                    baseDefinition.DeleteOperation,
                    resourceType,
                    baseDefinition.SearchParameters,
                    baseDefinition.Url,
                    baseDefinition.BaseUrl,
                    baseDefinition.ParentRequestId,
                    numResources,
                    baseDefinition.VersionType);
            }

            return null;
        }
    }
}
