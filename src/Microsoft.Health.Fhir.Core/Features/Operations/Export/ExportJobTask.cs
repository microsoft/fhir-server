// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportJobTask : IExportJobTask
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly IScoped<IAnonymizerFactory> _anonymizerFactory;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IGroupMemberExtractor _groupMemberExtractor;
        private readonly IResourceToByteArraySerializer _resourceToByteArraySerializer;
        private readonly IExportDestinationClient _exportDestinationClient;
        private readonly IResourceDeserializer _resourceDeserializer;
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
            IGroupMemberExtractor groupMemberExtractor,
            IResourceToByteArraySerializer resourceToByteArraySerializer,
            IExportDestinationClient exportDestinationClient,
            IResourceDeserializer resourceDeserializer,
            IScoped<IAnonymizerFactory> anonymizerFactory,
            ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(groupMemberExtractor, nameof(groupMemberExtractor));
            EnsureArg.IsNotNull(resourceToByteArraySerializer, nameof(resourceToByteArraySerializer));
            EnsureArg.IsNotNull(exportDestinationClient, nameof(exportDestinationClient));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _searchServiceFactory = searchServiceFactory;
            _groupMemberExtractor = groupMemberExtractor;
            _resourceToByteArraySerializer = resourceToByteArraySerializer;
            _resourceDeserializer = resourceDeserializer;
            _exportDestinationClient = exportDestinationClient;
            _anonymizerFactory = anonymizerFactory;
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
                ExportJobConfiguration exportJobConfiguration = _exportJobConfiguration;

                string connectionHash = string.IsNullOrEmpty(_exportJobConfiguration.StorageAccountConnection) ?
                    string.Empty :
                    Microsoft.Health.Core.Extensions.StringExtensions.ComputeHash(_exportJobConfiguration.StorageAccountConnection);

                if (string.IsNullOrEmpty(exportJobRecord.StorageAccountUri))
                {
                    if (!string.Equals(exportJobRecord.StorageAccountConnectionHash, connectionHash, StringComparison.Ordinal))
                    {
                        throw new DestinationConnectionException("Storage account connection string was updated during an export job.", HttpStatusCode.BadRequest);
                    }
                }
                else
                {
                    exportJobConfiguration = new ExportJobConfiguration();
                    exportJobConfiguration.Enabled = _exportJobConfiguration.Enabled;
                    exportJobConfiguration.StorageAccountUri = exportJobRecord.StorageAccountUri;
                }

                // Connect to export destination using appropriate client.
                await _exportDestinationClient.ConnectAsync(exportJobConfiguration, cancellationToken, _exportJobRecord.Id);

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If it is null, then we know we are processing a new job.
                if (_exportJobRecord.Progress == null)
                {
                    _exportJobRecord.Progress = new ExportJobProgress(continuationToken: null, page: 0);
                }

                // The intial list of query parameters will not have a continutation token. We will add that later if we get one back
                // from the search result.
                var queryParametersList = new List<Tuple<string, string>>()
                {
                    Tuple.Create(KnownQueryParameterNames.Count, _exportJobRecord.MaximumNumberOfResourcesPerQuery.ToString(CultureInfo.InvariantCulture)),
                    Tuple.Create(KnownQueryParameterNames.LastUpdated, $"le{_exportJobRecord.QueuedTime.ToString("o", CultureInfo.InvariantCulture)}"),
                };

                if (_exportJobRecord.Since != null)
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.LastUpdated, $"ge{_exportJobRecord.Since}"));
                }

                ExportJobProgress progress = _exportJobRecord.Progress;

                await RunExportSearch(exportJobConfiguration, progress, queryParametersList, cancellationToken);

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

                _exportJobRecord.FailureDetails = new JobFailureDetails(dce.Message, dce.StatusCode);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (ResourceNotFoundException rnfe)
            {
                _logger.LogError(rnfe, "Can't find specified resource. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(rnfe.Message, HttpStatusCode.BadRequest);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (FailedToParseAnonymizationConfigurationException ex)
            {
                _logger.LogError(ex, "Failed to parse anonymization configuration. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(ex.Message, HttpStatusCode.BadRequest);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (AnonymizationConfigurationNotFoundException ex)
            {
                _logger.LogError(ex, "Cannot found anonymization configuration. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(ex.Message, HttpStatusCode.BadRequest);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (AnonymizationConfigurationFetchException ex)
            {
                _logger.LogError(ex, "Failed to fetch anonymization configuration file. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(ex.Message, HttpStatusCode.BadRequest);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (Exception ex)
            {
                // The job has encountered an error it cannot recover from.
                // Try to update the job to failed state.
                _logger.LogError(ex, "Encountered an unhandled exception. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(Resources.UnknownError, HttpStatusCode.InternalServerError);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
        }

        private async Task CompleteJobAsync(OperationStatus completionStatus, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = completionStatus;
            _exportJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobRecordAsync(cancellationToken);
            _logger.LogInformation($"Export job completed. Id: {_exportJobRecord.Id}, Queued Time: {_exportJobRecord.QueuedTime}, End Time: {_exportJobRecord.EndTime}, IsAnonymizedExport: {IsAnonymizedExportJob()}");
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

        private async Task RunExportSearch(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress progress,
            List<Tuple<string, string>> sharedQueryParametersList,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(exportJobConfiguration, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(progress, nameof(progress));
            EnsureArg.IsNotNull(sharedQueryParametersList, nameof(sharedQueryParametersList));

            // Current batch will be used to organize a set of search results into a group so that they can be committed together.
            string currentBatchId = progress.Page.ToString("d6");

            List<Tuple<string, string>> queryParametersList = new List<Tuple<string, string>>(sharedQueryParametersList);
            if (progress.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken));
            }

            if (_exportJobRecord.ExportType == ExportJobType.Patient)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, KnownResourceTypes.Patient));
            }
            else if (_exportJobRecord.ExportType == ExportJobType.All && !string.IsNullOrEmpty(_exportJobRecord.ResourceType))
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, _exportJobRecord.ResourceType));
            }

            IAnonymizer anonymizer = IsAnonymizedExportJob() ? await CreateAnonymizerAsync(cancellationToken) : null;

            // Process the export if:
            // 1. There is continuation token, which means there is more resource to be exported.
            // 2. There is no continuation token but the page is 0, which means it's the initial export.
            while (progress.ContinuationToken != null || progress.Page == 0)
            {
                SearchResult searchResult = null;

                // Search and process the results.
                switch (_exportJobRecord.ExportType)
                {
                    case ExportJobType.All:
                    case ExportJobType.Patient:
                        using (IScoped<ISearchService> searchService = _searchServiceFactory())
                        {
                            searchResult = await searchService.Value.SearchAsync(
                                resourceType: null,
                                queryParametersList,
                                cancellationToken);
                        }

                        break;
                    case ExportJobType.Group:
                        searchResult = await GetGroupPatients(
                            _exportJobRecord.GroupId,
                            queryParametersList,
                            _exportJobRecord.QueuedTime,
                            cancellationToken);
                        break;
                }

                if (_exportJobRecord.ExportType == ExportJobType.Patient || _exportJobRecord.ExportType == ExportJobType.Group)
                {
                    uint resultIndex = 0;
                    foreach (SearchResultEntry result in searchResult.Results)
                    {
                        // If a job is resumed in the middle of processing patient compartment resources it will skip patients it has already exported compartment information for.
                        // This assumes the order of the search results is the same every time the same search is performed.
                        if (progress.SubSearch != null && result.Resource.ResourceId != progress.SubSearch.TriggeringResourceId)
                        {
                            resultIndex++;
                            continue;
                        }

                        if (progress.SubSearch == null)
                        {
                            progress.NewSubSearch(result.Resource.ResourceId);
                        }

                        await RunExportCompartmentSearch(exportJobConfiguration, progress.SubSearch, sharedQueryParametersList, cancellationToken, currentBatchId + ":" + resultIndex.ToString("d6"));
                        resultIndex++;

                        progress.ClearSubSearch();
                    }
                }

                await ProcessSearchResultsAsync(searchResult.Results, currentBatchId, anonymizer, cancellationToken);

                if (searchResult.ContinuationToken == null)
                {
                    // No more continuation token, we are done.
                    break;
                }

                await ProcessProgressChange(
                    exportJobConfiguration,
                    progress,
                    queryParametersList,
                    searchResult.ContinuationToken,
                    forceCommit: _exportJobRecord.ExportType == ExportJobType.Patient || _exportJobRecord.ExportType == ExportJobType.Group,
                    cancellationToken);
                currentBatchId = progress.Page.ToString("d6");
            }

            // Commit one last time for any pending changes.
            await _exportDestinationClient.CommitAsync(exportJobConfiguration, cancellationToken);
        }

        private async Task<IAnonymizer> CreateAnonymizerAsync(CancellationToken cancellationToken)
        {
            string configurationWithEtag = $"{_exportJobRecord.AnonymizationConfigurationLocation}:{_exportJobRecord.AnonymizationConfigurationFileETag}";

            return await _anonymizerFactory.Value.CreateAnonymizerAsync(configurationWithEtag, cancellationToken);
        }

        private async Task RunExportCompartmentSearch(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress progress,
            List<Tuple<string, string>> sharedQueryParametersList,
            CancellationToken cancellationToken,
            string batchIdPrefix = "")
        {
            EnsureArg.IsNotNull(exportJobConfiguration, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(progress, nameof(progress));
            EnsureArg.IsNotNull(sharedQueryParametersList, nameof(sharedQueryParametersList));

            // Current batch will be used to organize a set of search results into a group so that they can be committed together.
            string currentBatchId = batchIdPrefix + "-" + progress.Page.ToString("d6");

            List<Tuple<string, string>> queryParametersList = new List<Tuple<string, string>>(sharedQueryParametersList);
            if (progress.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken));
            }

            if (!string.IsNullOrEmpty(_exportJobRecord.ResourceType))
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, _exportJobRecord.ResourceType));
            }

            // Process the export if:
            // 1. There is continuation token, which means there is more resource to be exported.
            // 2. There is no continuation token but the page is 0, which means it's the initial export.
            while (progress.ContinuationToken != null || progress.Page == 0)
            {
                SearchResult searchResult = null;

                // Search and process the results.
                using (IScoped<ISearchService> searchService = _searchServiceFactory())
                {
                    searchResult = await searchService.Value.SearchCompartmentAsync(
                        compartmentType: KnownResourceTypes.Patient,
                        compartmentId: progress.TriggeringResourceId,
                        resourceType: null,
                        queryParametersList,
                        cancellationToken);
                }

                await ProcessSearchResultsAsync(searchResult.Results, currentBatchId, null, cancellationToken);

                if (searchResult.ContinuationToken == null)
                {
                    // No more continuation token, we are done.
                    break;
                }

                await ProcessProgressChange(exportJobConfiguration, progress, queryParametersList, searchResult.ContinuationToken, false, cancellationToken);
                currentBatchId = batchIdPrefix + '-' + progress.Page.ToString("d6");
            }

            // Commit one last time for any pending changes.
            await _exportDestinationClient.CommitAsync(exportJobConfiguration, cancellationToken);
            await UpdateJobRecordAsync(cancellationToken);
        }

        private async Task ProcessSearchResultsAsync(IEnumerable<SearchResultEntry> searchResults, string partId, IAnonymizer anonymizer, CancellationToken cancellationToken)
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

                ResourceElement element = _resourceDeserializer.Deserialize(resourceWrapper);

                if (anonymizer != null)
                {
                    element = anonymizer.Anonymize(element);
                }

                // Serialize into NDJson and write to the file.
                byte[] bytesToWrite = _resourceToByteArraySerializer.Serialize(element);

                await _exportDestinationClient.WriteFilePartAsync(exportFileInfo.FileUri, partId, bytesToWrite, cancellationToken);

                // Increment the file information.
                exportFileInfo.IncrementCount(bytesToWrite.Length);
            }
        }

        private async Task ProcessProgressChange(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress progress,
            List<Tuple<string, string>> queryParametersList,
            string continuationToken,
            bool forceCommit,
            CancellationToken cancellationToken)
        {
            // Update the continuation token in local cache and queryParams.
            // We will add or udpate the continuation token in the query parameters list.
            progress.UpdateContinuationToken(continuationToken);

            bool replacedContinuationToken = false;
            for (int index = 0; index < queryParametersList.Count; index++)
            {
                if (queryParametersList[index].Item1 == KnownQueryParameterNames.ContinuationToken)
                {
                    queryParametersList[index] = Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken);
                    replacedContinuationToken = true;
                }
            }

            if (!replacedContinuationToken)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken));
            }

            if (progress.Page % _exportJobRecord.NumberOfPagesPerCommit == 0 || forceCommit)
            {
                // Commit the changes.
                await _exportDestinationClient.CommitAsync(exportJobConfiguration, cancellationToken);

                // Update the job record.
                await UpdateJobRecordAsync(cancellationToken);
            }
        }

        private bool IsAnonymizedExportJob()
        {
            return !string.IsNullOrEmpty(_exportJobRecord.AnonymizationConfigurationLocation);
        }

        private async Task<SearchResult> GetGroupPatients(string groupId, List<Tuple<string, string>> queryParametersList, DateTimeOffset groupMembershipTime, CancellationToken cancellationToken)
        {
            if (!queryParametersList.Exists((Tuple<string, string> parameter) => parameter.Item1 == KnownQueryParameterNames.Id || parameter.Item1 == KnownQueryParameterNames.ContinuationToken))
            {
                var patientIds = await _groupMemberExtractor.GetGroupPatientIds(groupId, groupMembershipTime, cancellationToken);
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Id, string.Join(',', patientIds)));
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                return await searchService.Value.SearchAsync(
                               resourceType: null,
                               queryParametersList,
                               cancellationToken);
            }
        }
    }
}
