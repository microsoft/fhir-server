// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportJobTask : IExportJobTask
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly ISecretStore _secretStore;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ISearchService _searchService;
        private readonly IResourceToByteArraySerializer _resourceToByteArraySerializer;
        private readonly IExportDestinationClientFactory _exportDestinationClientFactory;
        private readonly ILogger _logger;

        // Currently we will have only one file per resource type. In the future we will add the ability to split
        // individual files based on a max file size. This could result in a single resource having multiple files.
        // We will have to update the below mapping to support multiple ExportFileInfo per resource type.
        private IDictionary<string, ExportFileInfo> _resourceTypeToFileInfoMapping;

        private ExportJobRecord _exportJobRecord;
        private WeakETag _weakETag;
        private IExportDestinationClient _exportDestinationClient;

        public ExportJobTask(
            IFhirOperationDataStore fhirOperationDataStore,
            ISecretStore secretStore,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ISearchService searchService,
            IResourceToByteArraySerializer resourceToByteArraySerializer,
            IExportDestinationClientFactory exportDestinationClientFactory,
            ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(secretStore, nameof(secretStore));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(resourceToByteArraySerializer, nameof(resourceToByteArraySerializer));
            EnsureArg.IsNotNull(exportDestinationClientFactory, nameof(exportDestinationClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStore = fhirOperationDataStore;
            _secretStore = secretStore;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _searchService = searchService;
            _resourceToByteArraySerializer = resourceToByteArraySerializer;
            _exportDestinationClientFactory = exportDestinationClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            _exportJobRecord = exportJobRecord;
            _weakETag = weakETag;

            try
            {
                // Get destination type from secret store and connect to the destination using appropriate client.
                await GetDestinationInfoAndConnect(cancellationToken);

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If it is null, then we know we are processing a new job.
                if (_exportJobRecord.Progress != null)
                {
                    await _exportDestinationClient.InitializeForResumingExportAsync(cancellationToken);
                }
                else
                {
                    _exportJobRecord.Progress = new ExportJobProgress(continuationToken: null, page: 0);
                }

                // Update our internal mapping with list of files that we have already committed (when resuming export jobs)
                // or an empty dictionary (for new export jobs). As we keep modifying our internal mapping, the Output field
                // will also get updated. These changes will be committed to the data store whenever we update our ExportJobRecord.
                _resourceTypeToFileInfoMapping = _exportJobRecord.Output;
                ExportJobProgress progress = _exportJobRecord.Progress;

                // Current batch will be used to organize a set of search results into a group so that they can be committed together.
                uint currentBatchId = progress.Page;

                // The first item is placeholder for continuation token so that it can be updated efficiently later.
                var queryParameters = new Tuple<string, string>[]
                {
                    Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken),
                    Tuple.Create(KnownQueryParameterNames.Count, _exportJobConfiguration.MaximumNumberOfResourcesPerQuery.ToString(CultureInfo.InvariantCulture)),
                    Tuple.Create(KnownQueryParameterNames.LastUpdated, $"le{_exportJobRecord.QueuedTime.ToString("o", CultureInfo.InvariantCulture)}"),
                };

                // Process the export if:
                // 1. There is continuation token, which means there is more resource to be exported.
                // 2. There is no continuation token but the page is 0, which means it's the initial export.
                while (progress.ContinuationToken != null || progress.Page == 0)
                {
                    // Search and process the results.
                    SearchResult searchResult = await _searchService.SearchAsync(_exportJobRecord.ResourceType, queryParameters, cancellationToken);
                    await ProcessSearchResults(searchResult.Results, currentBatchId, cancellationToken);

                    if (searchResult.ContinuationToken == null)
                    {
                        // No more continuation token, we are done.
                        break;
                    }

                    // Update the continuation token (local cache).
                    progress.UpdateContinuationToken(searchResult.ContinuationToken);
                    queryParameters[0] = Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken);

                    // Commit the changes if necessary.
                    if (progress.Page % _exportJobConfiguration.NumberOfPagesPerCommit == 0)
                    {
                        await _exportDestinationClient.CommitAsync(cancellationToken);

                        // Update the job record.
                        await UpdateAndCommitJobRecord(_exportJobRecord, cancellationToken);

                        currentBatchId = progress.Page;
                    }
                }

                // Commit one last time for any pending changes.
                await _exportDestinationClient.CommitAsync(cancellationToken);

                await UpdateAndCommitJobStatus(OperationStatus.Completed, updateEndTimestamp: true, cancellationToken);
                _logger.LogTrace("Successfully completed the job.");

                try
                {
                    // Best effort to delete the secret. If it fails to delete, then move on.
                    await _secretStore.DeleteSecretAsync(_exportJobRecord.SecretName, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete the secret.");
                }
            }
            catch (JobConflictException)
            {
                // The job was updated by another process.
                _logger.LogWarning("The job was updated by another process.");

                // TODO: We will want to get the latest and merge the results without updating the status.
                return;
            }
            catch (Exception ex)
            {
                // The job has encountered an error it cannot recover from.
                // Try to update the job to failed state.
                _logger.LogError(ex, "Encountered an unhandled exception. The job will be marked as failed.");

                await UpdateAndCommitJobStatus(OperationStatus.Failed, updateEndTimestamp: true, cancellationToken);
            }
        }

        private async Task UpdateAndCommitJobStatus(OperationStatus operationStatus, bool updateEndTimestamp, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = operationStatus;

            if (updateEndTimestamp)
            {
                _exportJobRecord.EndTime = Clock.UtcNow;
            }

            await UpdateAndCommitJobRecord(_exportJobRecord, cancellationToken);
        }

        private async Task UpdateAndCommitJobRecord(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            ExportJobOutcome updatedExportJobOutcome = await _fhirOperationDataStore.UpdateExportJobAsync(jobRecord, _weakETag, cancellationToken);

            _exportJobRecord = updatedExportJobOutcome.JobRecord;
            _weakETag = updatedExportJobOutcome.ETag;
        }

        // Get destination info from secret store, create appropriate export client and connect to destination.
        private async Task GetDestinationInfoAndConnect(CancellationToken cancellationToken)
        {
            SecretWrapper secret = await _secretStore.GetSecretAsync(_exportJobRecord.SecretName, cancellationToken);

            DestinationInfo destinationInfo = JsonConvert.DeserializeObject<DestinationInfo>(secret.SecretValue);

            _exportDestinationClient = _exportDestinationClientFactory.Create(destinationInfo.DestinationType);

            await _exportDestinationClient.ConnectAsync(destinationInfo.DestinationConnectionString, cancellationToken, _exportJobRecord.Id);
        }

        private async Task ProcessSearchResults(IEnumerable<ResourceWrapper> searchResults, uint partId, CancellationToken cancellationToken)
        {
            foreach (ResourceWrapper resourceWrapper in searchResults)
            {
                string resourceType = resourceWrapper.ResourceTypeName;

                // Check whether we already have an existing file for the current resource type.
                if (!_resourceTypeToFileInfoMapping.TryGetValue(resourceType, out ExportFileInfo exportFileInfo))
                {
                    string fileName = resourceType + ".ndjson";

                    Uri fileUri = await _exportDestinationClient.CreateFileAsync(fileName, cancellationToken);

                    exportFileInfo = new ExportFileInfo(resourceType, fileUri, sequence: 0);

                    _resourceTypeToFileInfoMapping.Add(resourceType, exportFileInfo);
                }

                // Serialize into NDJson and write to the file.
                byte[] bytesToWrite = _resourceToByteArraySerializer.Serialize(resourceWrapper);

                await _exportDestinationClient.WriteFilePartAsync(exportFileInfo.FileUri, partId, bytesToWrite, cancellationToken);

                // Increment the file information.
                exportFileInfo.IncrementCount(bytesToWrite.Length);
            }
        }
    }
}
