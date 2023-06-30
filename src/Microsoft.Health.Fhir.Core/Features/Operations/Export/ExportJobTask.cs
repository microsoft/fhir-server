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
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
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
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
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
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
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

            UpdateExportJob = UpdateExportJobAsync;
        }

        public Func<ExportJobRecord, WeakETag, CancellationToken, Task<ExportJobOutcome>> UpdateExportJob
        {
            get; set;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            _exportJobRecord = exportJobRecord;
            _weakETag = weakETag;
            _fileManager = new ExportFileManager(_exportJobRecord, _exportDestinationClient);

            var existingFhirRequestContext = _contextAccessor.RequestContext;

            try
            {
                _exportJobRecord.Status = OperationStatus.Running;

                ExportJobConfiguration exportJobConfiguration = _exportJobConfiguration;

                // Add a request context so that bundle issues can be added by the SearchOptionFactory
                var fhirRequestContext = new FhirRequestContext(
                    method: "Export",
                    uriString: "$export",
                    baseUriString: "$export",
                    correlationId: _exportJobRecord.Id,
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                };

                _contextAccessor.RequestContext = fhirRequestContext;

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
                    throw new BadRequestException(Core.Resources.TypeFilterWithoutTypeIsUnsupported);
                }

                // Connect to export destination using appropriate client.
                await _exportDestinationClient.ConnectAsync(exportJobConfiguration, cancellationToken, _exportJobRecord.StorageAccountContainerName);

                // If we are resuming a job, we can detect that by checking the progress info from the job record.
                // If it is null, then we know we are processing a new job.
                if (_exportJobRecord.Progress == null)
                {
                    _exportJobRecord.StartTime = Clock.UtcNow;
                    _exportJobRecord.Progress = new ExportJobProgress(continuationToken: null, page: 0);
                }
                else
                {
                    _exportJobRecord.RestartCount++;
                }

                // The initial list of query parameters will not have a continuation token. We will add that later if we get one back
                // from the search result.
                // As Till is a new property QueuedTime is being used as a backup incase Till doesn't exist in the job record.
                var tillTime = _exportJobRecord.Till != null ? _exportJobRecord.Till : new PartialDateTime(_exportJobRecord.QueuedTime);
                var queryParametersList = new List<Tuple<string, string>>()
                {
                    Tuple.Create(KnownQueryParameterNames.Count, _exportJobRecord.MaximumNumberOfResourcesPerQuery.ToString(CultureInfo.InvariantCulture)),
                    Tuple.Create(KnownQueryParameterNames.LastUpdated, $"le{tillTime}"),
                };

                if (_exportJobRecord.GlobalEndSurrogateId != null) // no need to check individually as they all should have values if anyone does
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.GlobalEndSurrogateId, _exportJobRecord.GlobalEndSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.EndSurrogateId, _exportJobRecord.EndSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.GlobalStartSurrogateId, _exportJobRecord.GlobalStartSurrogateId));
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.StartSurrogateId, _exportJobRecord.StartSurrogateId));
                }

                if (_exportJobRecord.Since != null)
                {
                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.LastUpdated, $"ge{_exportJobRecord.Since}"));
                }

                ExportJobProgress progress = _exportJobRecord.Progress;

                await RunExportSearch(exportJobConfiguration, progress, queryParametersList, cancellationToken);

                await CompleteJobAsync(OperationStatus.Completed, cancellationToken);

                _logger.LogTrace("Successfully completed the job.");
            }
            catch (JobSegmentCompletedException)
            {
                await CompleteJobAsync(OperationStatus.Completed, cancellationToken);

                _logger.LogTrace("Successfully completed a segment of the job.");
            }
            catch (JobConflictException)
            {
                // The export job was updated externally. There might be some additional resources that were exported
                // but we will not be updating the job record.
                _logger.LogTrace("The job was updated by another process.");
            }
            catch (RequestRateExceededException)
            {
                _logger.LogTrace("Job failed due to RequestRateExceeded.");
            }
            catch (DestinationConnectionException dce)
            {
                _logger.LogInformation(dce, "Can't connect to destination. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(dce.Message, dce.StatusCode);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (ResourceNotFoundException rnfe)
            {
                if (rnfe.ResourceKey?.ResourceType == KnownResourceTypes.Group)
                {
                    _logger.LogInformation(rnfe, "Can't find specified resource. The job will be marked as failed.");
                }
                else
                {
                    _logger.LogError(rnfe, "Can't find specified resource. The job will be marked as failed.");
                }

                _exportJobRecord.FailureDetails = new JobFailureDetails(rnfe.Message, HttpStatusCode.BadRequest);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (FailedToParseAnonymizationConfigurationException ex)
            {
                _logger.LogError(ex, "Failed to parse anonymization configuration. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(ex.Message, HttpStatusCode.BadRequest);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (FailedToAnonymizeResourceException ex)
            {
                _logger.LogError(ex, "Failed to anonymize resource. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(string.Format(Core.Resources.FailedToAnonymizeResource, ex.Message), HttpStatusCode.BadRequest);
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
            catch (RequestEntityTooLargeException retle)
            {
                _logger.LogError(retle, "Unable to update the ExportJobRecord as it exceeds CosmosDb document max size. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(Core.Resources.RequestEntityTooLargeExceptionDuringExport, HttpStatusCode.RequestEntityTooLarge);

                // Need to remove output records in order to make the export job record savable in the database.
                _exportJobRecord.Output.Clear();
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            catch (Exception ex)
            {
                // The job has encountered an error it cannot recover from.
                // Try to update the job to failed state.
                _logger.LogError(ex, "Encountered an unhandled exception. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(Core.Resources.UnknownError, HttpStatusCode.InternalServerError, string.Concat(ex.Message + "\n\r" + ex.StackTrace));
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
            finally
            {
                _contextAccessor.RequestContext = existingFhirRequestContext;
            }
        }

        private async Task CompleteJobAsync(OperationStatus completionStatus, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = completionStatus;
            _exportJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobRecordAsync(cancellationToken);
            string id = _exportJobRecord.Id ?? string.Empty;
            string status = _exportJobRecord.Status.ToString();
            string queuedTime = _exportJobRecord.QueuedTime.ToString("u") ?? string.Empty;
            string endTime = _exportJobRecord.EndTime?.ToString("u") ?? string.Empty;
            long dataSize = _exportJobRecord.Output?.Values.Sum(fileList => fileList.Sum(job => job?.CommittedBytes ?? 0)) ?? 0;
            bool isAnonymizedExport = IsAnonymizedExportJob();

            _logger.LogInformation(
                "Export job completed. Id: {Id}, Status {Status}, Queued Time: {QueuedTime}, End Time: {EndTime}, DataSize: {DataSize}, IsAnonymizedExport: {IsAnonymizedExport}",
                id,
                status,
                queuedTime,
                endTime,
                dataSize,
                isAnonymizedExport);

            await _mediator.Publish(new ExportTaskMetricsNotification(_exportJobRecord), CancellationToken.None);
        }

        private async Task UpdateJobRecordAsync(CancellationToken cancellationToken)
        {
            if (_contextAccessor?.RequestContext?.BundleIssues != null)
            {
                foreach (OperationOutcomeIssue issue in _contextAccessor.RequestContext.BundleIssues)
                {
                    _exportJobRecord.Issues.Add(issue);
                }
            }

            ExportJobOutcome updatedExportJobOutcome = await UpdateExportJob(_exportJobRecord, _weakETag, cancellationToken);
            _exportJobRecord = updatedExportJobOutcome.JobRecord;
            _weakETag = updatedExportJobOutcome.ETag;

            _contextAccessor.RequestContext.BundleIssues.Clear();
        }

        private async Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            using (IScoped<IFhirOperationDataStore> fhirOperationDataStore = _fhirOperationDataStoreFactory())
            {
                return await fhirOperationDataStore.Value.UpdateExportJobAsync(exportJobRecord, weakETag, cancellationToken);
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
                await ProcessFilter(exportJobConfiguration, progress, queryParametersList, sharedQueryParametersList, anonymizer, cancellationToken);
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
                        await ProcessFilter(exportJobConfiguration, progress, queryParametersList, sharedQueryParametersList, anonymizer, cancellationToken);
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

                await SearchWithFilter(exportJobConfiguration, progress, null, queryParametersList, sharedQueryParametersList, anonymizer, cancellationToken);
            }
        }

        private async Task ProcessFilter(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress exportJobProgress,
            List<Tuple<string, string>> queryParametersList,
            List<Tuple<string, string>> sharedQueryParametersList,
            IAnonymizer anonymizer,
            CancellationToken cancellationToken)
        {
            List<Tuple<string, string>> filterQueryParametersList = new List<Tuple<string, string>>(queryParametersList);
            foreach (var param in exportJobProgress.CurrentFilter.Parameters)
            {
                filterQueryParametersList.Add(param);
            }

            await SearchWithFilter(exportJobConfiguration, exportJobProgress, exportJobProgress.CurrentFilter.ResourceType, filterQueryParametersList, sharedQueryParametersList, anonymizer, cancellationToken);

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
            CancellationToken cancellationToken)
        {
            // Process the export if:
            // 1. There is continuation token, which means there is more resource to be exported.
            // 2. There is no continuation token but the page is 0, which means it's the initial export.
            while (progress.ContinuationToken != null || progress.Page == 0)
            {
                List<SearchResultEntry> searchResultEntries = new();
                string continuationToken = null;

                // Search and process the results.
                switch (_exportJobRecord.ExportType)
                {
                    case ExportJobType.All:
                        using (IScoped<ISearchService> searchService = _searchServiceFactory())
                        {
                            // Fetch all the expected results
                            do
                            {
                                var allSearchResult = await searchService.Value.SearchAsync(
                                    resourceType: resourceType,
                                    queryParametersList,
                                    cancellationToken,
                                    true);

                                searchResultEntries.AddRange(allSearchResult.Results);

                                queryParametersList.RemoveAll(x => x.Item1 == KnownQueryParameterNames.ContinuationToken);
                                continuationToken = allSearchResult.ContinuationToken;

                                if (continuationToken is not null)
                                {
                                    queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenConverter.Encode(continuationToken)));
                                }
                            }
                            while (searchResultEntries.Count < _exportJobRecord.MaximumNumberOfResourcesPerQuery || continuationToken is not null);
                        }

                        break;
                    case ExportJobType.Patient:
                        using (IScoped<ISearchService> searchService = _searchServiceFactory())
                        {
                            var patientSearchResult = await searchService.Value.SearchAsync(
                                resourceType: resourceType,
                                queryParametersList,
                                cancellationToken,
                                true);

                            searchResultEntries.AddRange(patientSearchResult.Results);
                            continuationToken = patientSearchResult.ContinuationToken;
                        }

                        break;
                    case ExportJobType.Group:
                        var groupSearchResult = await GetGroupPatients(
                            _exportJobRecord.GroupId,
                            queryParametersList,
                            _exportJobRecord.QueuedTime,
                            cancellationToken);

                        searchResultEntries.AddRange(groupSearchResult.Results);
                        continuationToken = groupSearchResult.ContinuationToken;

                        break;
                }

                if (_exportJobRecord.ExportType == ExportJobType.Patient || _exportJobRecord.ExportType == ExportJobType.Group)
                {
                    uint resultIndex = 0;
                    foreach (SearchResultEntry result in searchResultEntries)
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

                        await RunExportCompartmentSearch(exportJobConfiguration, progress.SubSearch, sharedQueryParametersList, anonymizer, cancellationToken);
                        resultIndex++;

                        progress.ClearSubSearch();
                    }
                }

                // Skips processing top level search results if the job only requested resources from the compartments of patients, but didn't want the patients.
                if (_exportJobRecord.ExportType == ExportJobType.All
                    || string.IsNullOrWhiteSpace(_exportJobRecord.ResourceType)
                    || _exportJobRecord.ResourceType.Contains(KnownResourceTypes.Patient, StringComparison.OrdinalIgnoreCase))
                {
                    ProcessSearchResults(searchResultEntries, anonymizer);
                }

                if (continuationToken == null)
                {
                    // No more continuation token, we are done.
                    break;
                }

                await ProcessProgressChange(
                    progress,
                    queryParametersList,
                    continuationToken,
                    false,
                    cancellationToken);
            }

            // Commit one last time for any pending changes.
            _fileManager.CommitFiles();
        }

        private async Task<IAnonymizer> CreateAnonymizerAsync(CancellationToken cancellationToken)
        {
            return await _anonymizerFactory.Value.CreateAnonymizerAsync(_exportJobRecord, cancellationToken);
        }

        private async Task RunExportCompartmentSearch(
            ExportJobConfiguration exportJobConfiguration,
            ExportJobProgress progress,
            List<Tuple<string, string>> sharedQueryParametersList,
            IAnonymizer anonymizer,
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

            if (progress.CurrentFilter != null)
            {
                await ProcessFilterForCompartment(progress, queryParametersList, anonymizer, cancellationToken);
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
                        await ProcessFilterForCompartment(progress, queryParametersList, anonymizer, cancellationToken);
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

                await SearchCompartmentWithFilter(progress, null, queryParametersList, anonymizer, cancellationToken);
            }
        }

        private async Task ProcessFilterForCompartment(
            ExportJobProgress exportJobProgress,
            List<Tuple<string, string>> queryParametersList,
            IAnonymizer anonymizer,
            CancellationToken cancellationToken)
        {
            List<Tuple<string, string>> filterQueryParametersList = new List<Tuple<string, string>>(queryParametersList);
            foreach (var param in exportJobProgress.CurrentFilter.Parameters)
            {
                filterQueryParametersList.Add(param);
            }

            await SearchCompartmentWithFilter(exportJobProgress, exportJobProgress.CurrentFilter.ResourceType, filterQueryParametersList, anonymizer, cancellationToken);
        }

        private async Task SearchCompartmentWithFilter(
            ExportJobProgress progress,
            string resourceType,
            List<Tuple<string, string>> queryParametersList,
            IAnonymizer anonymizer,
            CancellationToken cancellationToken)
        {
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
                        cancellationToken,
                        true,
                        _exportJobRecord.SmartRequest);
                }

                ProcessSearchResults(searchResult.Results, anonymizer);

                if (searchResult.ContinuationToken == null)
                {
                    // No more continuation token, we are done.
                    break;
                }

                await ProcessProgressChange(progress, queryParametersList, searchResult.ContinuationToken, true, cancellationToken);
            }

            // Commit one last time for any pending changes.
            _fileManager.CommitFullFiles(_exportJobConfiguration.RollingFileSizeInMB * 1024 * 1024);

            progress.MarkFilterFinished();
        }

        private void ProcessSearchResults(IEnumerable<SearchResultEntry> searchResults, IAnonymizer anonymizer)
        {
            foreach (SearchResultEntry result in searchResults)
            {
                ResourceWrapper resourceWrapper = result.Resource;
                var data = result.Resource.RawResource.Data;

                if (anonymizer != null)
                {
                    ResourceElement element = _resourceDeserializer.Deserialize(resourceWrapper);
                    try
                    {
                        element = anonymizer.Anonymize(element);
                    }
                    catch (Exception ex)
                    {
                        throw new FailedToAnonymizeResourceException(ex.Message, ex);
                    }

                    // Serialize into NDJson and write to the file.
                    data = _resourceToByteArraySerializer.StringSerialize(element);
                }
                else if (!resourceWrapper.RawResource.IsMetaSet)
                {
                    // For older records in Cosmos the metadata isn't included in the raw resource
                    ResourceElement element = _resourceDeserializer.Deserialize(resourceWrapper);
                    data = _resourceToByteArraySerializer.StringSerialize(element);
                }

                _fileManager.WriteToFile(resourceWrapper.ResourceTypeName, data);
            }
        }

        private async Task ProcessProgressChange(
            ExportJobProgress progress,
            List<Tuple<string, string>> queryParametersList,
            string continuationToken,
            bool onlyCommitFull,
            CancellationToken cancellationToken)
        {
            // Update the continuation token in local cache and queryParams.
            // We will add or udpate the continuation token in the query parameters list.
            progress.UpdateContinuationToken(ContinuationTokenConverter.Encode(continuationToken));

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

            // Commit the changes.
            if (onlyCommitFull)
            {
                _fileManager.CommitFullFiles(_exportJobConfiguration.RollingFileSizeInMB * 1024 * 1024);
            }
            else
            {
                _fileManager.CommitFiles();

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
                HashSet<string> patientIds = await _groupMemberExtractor.GetGroupPatientIds(groupId, groupMembershipTime, cancellationToken);

                if (patientIds.Count == 0)
                {
                    _logger.LogInformation("Group: {GroupId} does not have any patient ids as members.", groupId);
                    return SearchResult.Empty();
                }

                queryParametersList.Add(Tuple.Create(KnownQueryParameterNames.Id, string.Join(',', patientIds)));
            }

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                return await searchService.Value.SearchAsync(
                               resourceType: KnownResourceTypes.Patient,
                               queryParametersList,
                               cancellationToken,
                               true);
            }
        }
    }
}
