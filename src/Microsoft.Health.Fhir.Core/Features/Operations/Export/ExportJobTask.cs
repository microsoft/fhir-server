// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly IResourceToNdjsonBytesSerializer _resourceToNdjsonSerializer;
        private readonly IExportDestinationClientFactory _exportDestinationClientFactory;
        private readonly ILogger _logger;

        // Currently we will have only one file per resource type. In the future we will add the ability to split
        // individual files based on a max file size. This could result in a single resource having multiple files.
        // We will have to update the below mapping to support multiple ExportFileInfo per resource type.
        private readonly Dictionary<string, ExportFileInfo> _resourceTypeToFileInfoMapping = new Dictionary<string, ExportFileInfo>();

        private ExportJobRecord _exportJobRecord;
        private WeakETag _weakETag;
        private IExportDestinationClient _exportDestinationClient;

        public ExportJobTask(
            IFhirOperationDataStore fhirOperationDataStore,
            ISecretStore secretStore,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ISearchService searchService,
            IResourceToNdjsonBytesSerializer resourceToNdjsonSerializer,
            IExportDestinationClientFactory exportDestinationClientFactory,
            ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(secretStore, nameof(secretStore));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(resourceToNdjsonSerializer, nameof(resourceToNdjsonSerializer));
            EnsureArg.IsNotNull(exportDestinationClientFactory, nameof(exportDestinationClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStore = fhirOperationDataStore;
            _secretStore = secretStore;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _searchService = searchService;
            _resourceToNdjsonSerializer = resourceToNdjsonSerializer;
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
                // Get destination type from secret store.
                DestinationInfo destinationInfo = await GetDestinationInfo();

                // Connect to the destination using appropriate client.
                _exportDestinationClient = _exportDestinationClientFactory.Create(destinationInfo.DestinationType);

                await _exportDestinationClient.ConnectAsync(destinationInfo.DestinationConnectionString, cancellationToken);

                // TODO: For now, always restart from the beginning. We will support resume in another work item.
                exportJobRecord.Progress = new ExportJobProgress(continuationToken: null, page: 0);

                ExportJobProgress progress = exportJobRecord.Progress;

                // Current page will be used to organize a set of search results into a group so that they can be committed together.
                uint currentPage = progress.Page;

                // The first item is placeholder for continuation token so that it can be updated efficiently later.
                var queryParameters = new Tuple<string, string>[]
                {
                    null,
                    Tuple.Create(KnownQueryParameterNames.Count, _exportJobConfiguration.MaxItemCountPerQuery.ToString(CultureInfo.InvariantCulture)),
                    Tuple.Create(KnownQueryParameterNames.LastUpdated, $"le{exportJobRecord.QueuedTime.ToString("o", CultureInfo.InvariantCulture)}"),
                };

                // Process the export if:
                // 1. There is continuation token, which means there is more resource to be exported.
                // 2. There is no continuation token but the page is 0, which means it's the initial export.
                while (progress.ContinuationToken != null || (progress.ContinuationToken == null && progress.Page == 0))
                {
                    // Commit the changes if necessary.
                    if (progress.Page != 0 && progress.Page % _exportJobConfiguration.NumberOfPagesPerCommit == 0)
                    {
                        await _exportDestinationClient.CommitAsync(cancellationToken);

                        // Update the job record.
                        await UpdateJobRecord(_exportJobRecord, cancellationToken);

                        currentPage = progress.Page;
                    }

                    // Set the continuation token.
                    queryParameters[0] = Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken);

                    SearchResult searchResult = await _searchService.SearchAsync(exportJobRecord.ResourceType, queryParameters, cancellationToken);

                    foreach (ResourceWrapper resourceWrapper in searchResult.Results)
                    {
                        await ProcessResourceWrapperAsync(resourceWrapper, currentPage, cancellationToken);
                    }

                    if (searchResult.ContinuationToken == null)
                    {
                        // No more continuation token, we are done.
                        break;
                    }

                    // Update the job record.
                    progress.UpdateContinuationToken(searchResult.ContinuationToken);
                }

                // Commit one last time for any pending changes.
                await _exportDestinationClient.CommitAsync(cancellationToken);

                _exportJobRecord.Output.AddRange(_resourceTypeToFileInfoMapping.Values);

                _logger.LogTrace("Successfully completed the job.");

                await UpdateJobStatus(OperationStatus.Completed, cancellationToken);

                try
                {
                    // Best effort to delete the secret. If it fails to delete, then move on.
                    await _secretStore.DeleteSecretAsync(_exportJobRecord.SecretName);
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

                await UpdateJobStatus(OperationStatus.Failed, cancellationToken);
            }
        }

        private async Task UpdateJobStatus(OperationStatus operationStatus, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = operationStatus;

            await UpdateJobRecord(_exportJobRecord, cancellationToken);
        }

        private async Task UpdateJobRecord(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            ExportJobOutcome updatedExportJobOutcome = await _fhirOperationDataStore.UpdateExportJobAsync(jobRecord, _weakETag, cancellationToken);

            _exportJobRecord = updatedExportJobOutcome.JobRecord;
            _weakETag = updatedExportJobOutcome.ETag;
        }

        private async Task<DestinationInfo> GetDestinationInfo()
        {
            SecretWrapper secret = await _secretStore.GetSecretAsync(_exportJobRecord.SecretName);

            DestinationInfo destinationInfo = JsonConvert.DeserializeObject<DestinationInfo>(secret.SecretValue);
            return destinationInfo;
        }

        private async Task ProcessResourceWrapperAsync(ResourceWrapper resourceWrapper, uint partId, CancellationToken cancellationToken)
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
            byte[] bytesToWrite = _resourceToNdjsonSerializer.Serialize(resourceWrapper);

            await _exportDestinationClient.WriteFilePartAsync(exportFileInfo.FileUri, partId, bytesToWrite, cancellationToken);

            // Incremenet the file information.
            exportFileInfo.Increment(bytesToWrite.Length);
        }
    }
}
