// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Text;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
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
        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _contextAccessor;
        private readonly ILogger _logger;

        private ExportJobRecord _exportJobRecord;
        private WeakETag _weakETag;
        private ExportFileManager _fileManager;

        public ExportJobTask(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            Func<IScoped<ISearchService>> searchServiceFactory,
            IGroupMemberExtractor groupMemberExtractor,
            IResourceToByteArraySerializer resourceToByteArraySerializer,
            IExportDestinationClient exportDestinationClient,
            IResourceDeserializer resourceDeserializer,
            IScoped<IAnonymizerFactory> anonymizerFactory,
            IMediator mediator,
            IFhirRequestContextAccessor contextAccessor,
            ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(groupMemberExtractor, nameof(groupMemberExtractor));
            EnsureArg.IsNotNull(resourceToByteArraySerializer, nameof(resourceToByteArraySerializer));
            EnsureArg.IsNotNull(exportDestinationClient, nameof(exportDestinationClient));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _searchServiceFactory = searchServiceFactory;
            _groupMemberExtractor = groupMemberExtractor;
            _resourceToByteArraySerializer = resourceToByteArraySerializer;
            _resourceDeserializer = resourceDeserializer;
            _exportDestinationClient = exportDestinationClient;
            _anonymizerFactory = anonymizerFactory;
            _mediator = mediator;
            _contextAccessor = contextAccessor;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            _exportJobRecord = exportJobRecord;
            _weakETag = weakETag;
            _fileManager = new ExportFileManager(_exportJobRecord, _exportDestinationClient);

            var existingFhirRequestContext = _contextAccessor.FhirRequestContext;

            try
            {
                ExportJobConfiguration exportJobConfiguration = _exportJobConfiguration;

                string connectionHash = string.IsNullOrEmpty(_exportJobConfiguration.StorageAccountConnection) ?
                    string.Empty :
                    Health.Core.Extensions.StringExtensions.ComputeHash(_exportJobConfiguration.StorageAccountConnection);

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

                if (_exportJobRecord.Filters != null &&
                    _exportJobRecord.Filters.Count > 0 &&
                    string.IsNullOrEmpty(_exportJobRecord.ResourceType))
                {
                    throw new BadRequestException(Resources.TypeFilterWithoutTypeIsUnsupported);
                }

                // Connect to export destination using appropriate client.
                await _exportDestinationClient.ConnectAsync(exportJobConfiguration, cancellationToken, _exportJobRecord.StorageAccountContainerName);

                // Add a request context so that bundle issues can be added by the SearchOptionFactory
                var fhirRequestContext = new FhirRequestContext(
                method: "Export",
                uriString: "$export",
                baseUriString: "$export",
                correlationId: _exportJobRecord.Id,
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());

                _contextAccessor.FhirRequestContext = fhirRequestContext;

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
            finally
            {
                _contextAccessor.FhirRequestContext = existingFhirRequestContext;
            }
        }

        private async Task CompleteJobAsync(OperationStatus completionStatus, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = completionStatus;
            _exportJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobRecordAsync(cancellationToken);
            _logger.LogInformation(ExtractExportTaskLoggingMessage());

            await _mediator.Publish(new ExportTaskMetricsNotification(_exportJobRecord), CancellationToken.None);
        }

        private async Task UpdateJobRecordAsync(CancellationToken cancellationToken)
        {
            foreach (OperationOutcomeIssue issue in _contextAccessor.FhirRequestContext.BundleIssues)
            {
                _exportJobRecord.Issues.Add(issue);
            }

            using (IScoped<IFhirOperationDataStore> fhirOperationDataStore = _fhirOperationDataStoreFactory())
            {
                ExportJobOutcome updatedExportJobOutcome = await fhirOperationDataStore.Value.UpdateExportJobAsync(_exportJobRecord, _weakETag, cancellationToken);

                _exportJobRecord = updatedExportJobOutcome.JobRecord;
                _weakETag = updatedExportJobOutcome.ETag;

                _contextAccessor.FhirRequestContext.BundleIssues.Clear();
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

            List<Tuple<string, string>> queryParametersList = new List<Tuple<string, string>>(sharedQueryParametersList);
            if (progress.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken));
            }

            var requestedResourceTypes = _exportJobRecord.ResourceType?.Split(',');
            var filteredResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_exportJobRecord.Filters != null)
            {
                foreach (var filter in _exportJobRecord.Filters)
                {
                    filteredResources.Add(filter.ResourceType);
                }
            }

            IAnonymizer anonymizer = IsAnonymizedExportJob() ? await CreateAnonymizerAsync(cancellationToken) : null;

            if (progress.CurrentFilter != null)
            {
                await ProcessFilter(exportJobConfiguration, progress, queryParametersList, sharedQueryParametersList, anonymizer, "filter", cancellationToken);
            }

            if (_exportJobRecord.Filters != null && _exportJobRecord.Filters.Any(filter => !progress.CompletedFilters.Contains(filter)))
            {
                foreach (var filter in _exportJobRecord.Filters)
                {
                    if (!progress.CompletedFilters.Contains(filter) &&
                        requestedResourceTypes != null &&
                        requestedResourceTypes.Contains(filter.ResourceType, StringComparison.OrdinalIgnoreCase) &&
                        (_exportJobRecord.ExportType == ExportJobType.All || filter.ResourceType.Equals(KnownResourceTypes.Patient, StringComparison.OrdinalIgnoreCase)))
                    {
                        progress.SetFilter(filter);
                        await ProcessFilter(exportJobConfiguration, progress, queryParametersList, sharedQueryParametersList, anonymizer, "filter", cancellationToken);
                    }
                }
            }

            // The unfiltered search should be run if there were no filters specified, there were types requested that didn't have filters for them, or if a Patient/Group level export didn't have filters for Patients.
            // Examples:
            // If a patient/group export job with type and type filters is run, but patients aren't in the types requested, the search should be run here but no patients printed to the output
            // If a patient/group export job with type and type filters is run, and patients are in the types requested and filtered, the search should not be run as patients were searched above
            // If an export job with type and type filters is run, the search should not be run if all the types were searched above.
            if (_exportJobRecord.Filters == null ||
                _exportJobRecord.Filters.Count == 0 ||
                (_exportJobRecord.ExportType == ExportJobType.All &&
                !requestedResourceTypes.All(resourceType => filteredResources.Contains(resourceType))) ||
                ((_exportJobRecord.ExportType == ExportJobType.Patient || _exportJobRecord.ExportType == ExportJobType.Group) &&
                !filteredResources.Contains(KnownResourceTypes.Patient)))
            {
                if (_exportJobRecord.ExportType == ExportJobType.Patient)
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, KnownResourceTypes.Patient));
                }
                else if (_exportJobRecord.ExportType == ExportJobType.All && requestedResourceTypes != null)
                {
                    List<string> resources = new List<string>();

                    foreach (var resource in requestedResourceTypes)
                    {
                        if (!filteredResources.Contains(resource))
                        {
                            resources.Add(resource);
                        }
                    }

                    if (resources.Count > 0)
                    {
                        queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, resources.JoinByOrSeparator()));
                    }
                }

                await SearchWithFilter(exportJobConfiguration, progress, null, queryParametersList, sharedQueryParametersList, anonymizer, string.Empty, cancellationToken);
            }
        }

        private async Task ProcessFilter(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress exportJobProgress,
            List<Tuple<string, string>> queryParametersList,
            List<Tuple<string, string>> sharedQueryParametersList,
            IAnonymizer anonymizer,
            string batchIdPrefix,
            CancellationToken cancellationToken)
        {
            var index = _exportJobRecord.Filters.IndexOf(exportJobProgress.CurrentFilter);
            List<Tuple<string, string>> filterQueryParametersList = new List<Tuple<string, string>>(queryParametersList);
            foreach (var param in exportJobProgress.CurrentFilter.Parameters)
            {
                filterQueryParametersList.Add(param);
            }

            await SearchWithFilter(exportJobConfiguration, exportJobProgress, exportJobProgress.CurrentFilter.ResourceType, filterQueryParametersList, sharedQueryParametersList, anonymizer, batchIdPrefix + index + "-", cancellationToken);

            exportJobProgress.MarkFilterFinished();
            await UpdateJobRecordAsync(cancellationToken);
        }

        private async Task SearchWithFilter(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress progress,
            string resourceType,
            List<Tuple<string, string>> queryParametersList,
            List<Tuple<string, string>> sharedQueryParametersList,
            IAnonymizer anonymizer,
            string batchIdPrefix,
            CancellationToken cancellationToken)
        {
            // Current batch will be used to organize a set of search results into a group so that they can be committed together.
            string currentBatchId = batchIdPrefix + progress.Page.ToString("d6");

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
                                resourceType: resourceType,
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

                // Skips processing top level search results if the job only requested resources from the compartments of patients, but didn't want the patients.
                if (_exportJobRecord.ExportType == ExportJobType.All
                    || string.IsNullOrWhiteSpace(_exportJobRecord.ResourceType)
                    || _exportJobRecord.ResourceType.Contains(KnownResourceTypes.Patient, StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessSearchResultsAsync(searchResult.Results, currentBatchId, anonymizer, cancellationToken);
                }

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
                currentBatchId = batchIdPrefix + progress.Page.ToString("d6");
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

            List<Tuple<string, string>> queryParametersList = new List<Tuple<string, string>>(sharedQueryParametersList);
            if (progress.ContinuationToken != null)
            {
                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, progress.ContinuationToken));
            }

            var requestedResourceTypes = _exportJobRecord.ResourceType?.Split(',');
            var filteredResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_exportJobRecord.Filters != null)
            {
                foreach (var filter in _exportJobRecord.Filters)
                {
                    filteredResources.Add(filter.ResourceType);
                }
            }

            if (progress.CurrentFilter != null)
            {
                await ProcessFilterForCompartment(exportJobConfiguration, progress, queryParametersList, batchIdPrefix + "-filter", cancellationToken);
            }

            if (_exportJobRecord.Filters != null)
            {
                foreach (var filter in _exportJobRecord.Filters)
                {
                    if (!progress.CompletedFilters.Contains(filter) &&
                        requestedResourceTypes != null &&
                        requestedResourceTypes.Contains(filter.ResourceType, StringComparison.OrdinalIgnoreCase))
                    {
                        progress.SetFilter(filter);
                        await ProcessFilterForCompartment(exportJobConfiguration, progress, queryParametersList, batchIdPrefix + "-filter", cancellationToken);
                    }
                }
            }

            if (_exportJobRecord.Filters == null ||
                _exportJobRecord.Filters.Count == 0 ||
                !requestedResourceTypes.All(resourceType => filteredResources.Contains(resourceType)))
            {
                if (requestedResourceTypes != null)
                {
                    List<string> resources = new List<string>();

                    foreach (var resource in requestedResourceTypes)
                    {
                        if (!filteredResources.Contains(resource))
                        {
                            resources.Add(resource);
                        }
                    }

                    if (resources.Count > 0)
                    {
                        queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Type, resources.JoinByOrSeparator()));
                    }
                }

                await SearchCompartmentWithFilter(exportJobConfiguration, progress, null, queryParametersList, batchIdPrefix, cancellationToken);
            }
        }

        private async Task ProcessFilterForCompartment(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress exportJobProgress,
            List<Tuple<string, string>> queryParametersList,
            string batchIdPrefix,
            CancellationToken cancellationToken)
        {
            var index = _exportJobRecord.Filters.IndexOf(exportJobProgress.CurrentFilter);
            List<Tuple<string, string>> filterQueryParametersList = new List<Tuple<string, string>>(queryParametersList);
            foreach (var param in exportJobProgress.CurrentFilter.Parameters)
            {
                filterQueryParametersList.Add(param);
            }

            await SearchCompartmentWithFilter(exportJobConfiguration, exportJobProgress, exportJobProgress.CurrentFilter.ResourceType, filterQueryParametersList, batchIdPrefix + index, cancellationToken);
        }

        private async Task SearchCompartmentWithFilter(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress progress,
            string resourceType,
            List<Tuple<string, string>> queryParametersList,
            string batchIdPrefix,
            CancellationToken cancellationToken)
        {
            // Current batch will be used to organize a set of search results into a group so that they can be committed together.
            string currentBatchId = batchIdPrefix + "-" + progress.Page.ToString("d6");

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
                        resourceType: resourceType,
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

            progress.MarkFilterFinished();
            await UpdateJobRecordAsync(cancellationToken);
        }

        private async Task ProcessSearchResultsAsync(IEnumerable<SearchResultEntry> searchResults, string partId, IAnonymizer anonymizer, CancellationToken cancellationToken)
        {
            foreach (SearchResultEntry result in searchResults)
            {
                ResourceWrapper resourceWrapper = result.Resource;
                ResourceElement element = _resourceDeserializer.Deserialize(resourceWrapper);

                if (anonymizer != null)
                {
                    element = anonymizer.Anonymize(element);
                }

                // Serialize into NDJson and write to the file.
                byte[] bytesToWrite = _resourceToByteArraySerializer.Serialize(element);

                await _fileManager.WriteToFile(resourceWrapper.ResourceTypeName, partId, bytesToWrite, cancellationToken);
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
            progress.UpdateContinuationToken(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(continuationToken)));

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

        private string ExtractExportTaskLoggingMessage()
        {
            string id = _exportJobRecord.Id ?? string.Empty;
            string status = _exportJobRecord.Status.ToString();
            string queuedTime = _exportJobRecord.QueuedTime.ToString("u") ?? string.Empty;
            string endTime = _exportJobRecord.EndTime?.ToString("u") ?? string.Empty;
            long dataSize = _exportJobRecord.Output?.Values.Sum(fileList => fileList.Sum(job => job?.CommittedBytes ?? 0)) ?? 0;
            bool isAnonymizedExport = IsAnonymizedExportJob();

            return $"Export job completed. Id: {id}, Status {status}, Queued Time: {queuedTime}, End Time: {endTime}, DataSize: {dataSize}, IsAnonymizedExport: {isAnonymizedExport}";
        }

        private async Task<SearchResult> GetGroupPatients(string groupId, List<Tuple<string, string>> queryParametersList, DateTimeOffset groupMembershipTime, CancellationToken cancellationToken)
        {
            if (!queryParametersList.Exists((Tuple<string, string> parameter) => parameter.Item1 == KnownQueryParameterNames.Id || parameter.Item1 == KnownQueryParameterNames.ContinuationToken))
            {
                HashSet<string> patientIds = await _groupMemberExtractor.GetGroupPatientIds(groupId, groupMembershipTime, cancellationToken);

                if (patientIds.Count == 0)
                {
                    _logger.LogInformation($"Group {groupId} does not have any patient ids as members.");
                    return SearchResult.Empty();
                }

                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Id, string.Join(',', patientIds)));
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                return await searchService.Value.SearchAsync(
                               resourceType: KnownResourceTypes.Patient,
                               queryParametersList,
                               cancellationToken);
            }
        }
    }
}
