// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportJobTask : IExportJobTask
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceToByteArraySerializer _resourceToByteArraySerializer;
        private readonly IExportDestinationClient _exportDestinationClient;
        private readonly ILogger _logger;

        // Currently we will have only one file per resource type. In the future we will add the ability to split
        // individual files based on a max file size. This could result in a single resource having multiple files.
        // We will have to update the below mapping to support multiple ExportFileInfo per resource type.
        private readonly IDictionary<string, ExportFileInfo> _resourceTypeToFileInfoMapping = new Dictionary<string, ExportFileInfo>();

        private ExportJobRecord _exportJobRecord;
        private WeakETag _weakETag;

        public ExportJobTask(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            Func<IScoped<ISearchService>> searchServiceFactory,
            IResourceToByteArraySerializer resourceToByteArraySerializer,
            IExportDestinationClient exportDestinationClient,
            ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(resourceToByteArraySerializer, nameof(resourceToByteArraySerializer));
            EnsureArg.IsNotNull(exportDestinationClient, nameof(exportDestinationClient));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _searchServiceFactory = searchServiceFactory;
            _resourceToByteArraySerializer = resourceToByteArraySerializer;
            _exportDestinationClient = exportDestinationClient;
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
                // Connect to export destination using appropriate client.
                await _exportDestinationClient.ConnectAsync(cancellationToken, _exportJobRecord.Id);

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If it is null, then we know we are processing a new job.
                if (_exportJobRecord.Progress == null)
                {
                    _exportJobRecord.Progress = new ExportJobProgress(continuationToken: null, page: 0);
                }

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
                    SearchResult searchResult;

                    // Search and process the results.
                    using (IScoped<ISearchService> searchService = _searchServiceFactory())
                    {
                        // If the continuation token is null, then we will exclude it. Calculate the offset and count to be passed in.
                        int offset = queryParameters[0].Item2 == null ? 1 : 0;

                        searchResult = await searchService.Value.SearchAsync(
                            _exportJobRecord.ResourceType,
                            new ArraySegment<Tuple<string, string>>(queryParameters, offset, queryParameters.Length - offset),
                            cancellationToken);
                    }

                    await ProcessSearchResultsAsync(searchResult.Results, currentBatchId, cancellationToken);

                    if (searchResult.ContinuationToken == null)
                    {
                        // No more continuation token, we are done.
                        break;
                    }

                    // Update the continuation token (local cache).
                    progress.UpdateContinuationToken(searchResult.ContinuationToken);
                    queryParameters[0] = Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken);

                    if (progress.Page % _exportJobConfiguration.NumberOfPagesPerCommit == 0)
                    {
                        // Commit the changes.
                        await _exportDestinationClient.CommitAsync(cancellationToken);

                        // Update the job record.
                        await UpdateJobRecordAsync(cancellationToken);

                        currentBatchId = progress.Page;
                    }
                }

                // Commit one last time for any pending changes.
                await _exportDestinationClient.CommitAsync(cancellationToken);

                await CompleteJobAsync(OperationStatus.Completed, cancellationToken);

                _logger.LogTrace("Successfully completed the job.");
            }
            catch (JobConflictException)
            {
                // The export job was updated externally. There might be some additional resources that were exported
                // but we will not be updating the job record.
                _logger.LogTrace("The job was updated by another process.");
            }
            catch (DestinationConnectionException dce)
            {
                _logger.LogError(dce, "Can't connect to destination. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new ExportJobFailureDetails(dce.Message, dce.StatusCode);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (Exception ex)
            {
                // The job has encountered an error it cannot recover from.
                // Try to update the job to failed state.
                _logger.LogError(ex, "Encountered an unhandled exception. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new ExportJobFailureDetails(Resources.UnknownError, HttpStatusCode.InternalServerError);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
        }

        private async Task CompleteJobAsync(OperationStatus completionStatus, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = completionStatus;
            _exportJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobRecordAsync(cancellationToken);
        }

        private async Task UpdateJobRecordAsync(CancellationToken cancellationToken)
        {
            using (IScoped<IFhirOperationDataStore> fhirOperationDataStore = _fhirOperationDataStoreFactory())
            {
                ExportJobOutcome updatedExportJobOutcome = await fhirOperationDataStore.Value.UpdateExportJobAsync(_exportJobRecord, _weakETag, cancellationToken);

                _exportJobRecord = updatedExportJobOutcome.JobRecord;
                _weakETag = updatedExportJobOutcome.ETag;
            }
        }

        private async Task ProcessSearchResultsAsync(IEnumerable<SearchResultEntry> searchResults, uint partId, CancellationToken cancellationToken)
        {
            foreach (SearchResultEntry result in searchResults)
            {
                ResourceWrapper resourceWrapper = result.Resource;

                string resourceType = resourceWrapper.ResourceTypeName;

                // Check whether we already have an existing file for the current resource type.
                if (!_resourceTypeToFileInfoMapping.TryGetValue(resourceType, out ExportFileInfo exportFileInfo))
                {
                    // Check whether we have seen this file previously (in situations where we are resuming an export)
                    if (_exportJobRecord.Output.TryGetValue(resourceType, out exportFileInfo))
                    {
                        // A file already exists for this resource type. Let us open the file on the client.
                        await _exportDestinationClient.OpenFileAsync(exportFileInfo.FileUri, cancellationToken);
                    }
                    else
                    {
                        // File does not exist. Create it.
                        string fileName = resourceType + ".ndjson";
                        Uri fileUri = await _exportDestinationClient.CreateFileAsync(fileName, cancellationToken);

                        exportFileInfo = new ExportFileInfo(resourceType, fileUri, sequence: 0);

                        // Since we created a new file the JobRecord Output also needs to know about it.
                        _exportJobRecord.Output.TryAdd(resourceType, exportFileInfo);
                    }

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
