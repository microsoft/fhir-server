// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly ILogger _logger;

        private ExportJobRecord _exportJobRecord;
        private WeakETag _weakETag;

        private static bool stop = false;
        private static long _resourcesTotal = 0L;
        private static Stopwatch _swReport = Stopwatch.StartNew();
        private static Stopwatch _sw = Stopwatch.StartNew();

        private static Stopwatch _database = new Stopwatch();
        private static Stopwatch _unzip = new Stopwatch();
        private static Stopwatch _blob = new Stopwatch();

        private static int _threads = 1;
        private static bool _readsEnabled = true;
        private static bool _writesEnabled = true;
        private static bool _decompressEnabled = true;
        private static int _reportingPeriodSec = 30;
        private ICompressedRawResourceConverter _compressedRawResourceConverter;

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
            ICompressedRawResourceConverter compressedRawResourceConverter,
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
            _compressedRawResourceConverter = compressedRawResourceConverter;
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
                string resourceType = _exportJobRecord.ResourceType;

                await _exportDestinationClient.ConnectAsync(cancellationToken, _exportJobRecord.StorageAccountContainerName);

                var startId = LastUpdatedToResourceSurrogateId(_exportJobRecord.Since.ToDateTimeOffset(
                    defaultMonth: 1,
                    defaultDaySelector: (year, month) => 1,
                    defaultHour: 0,
                    defaultMinute: 0,
                    defaultSecond: 0,
                    defaultFraction: 0.0000000m,
                    defaultUtcOffset: TimeSpan.Zero).UtcDateTime);
                var endId = LastUpdatedToResourceSurrogateId(DateTime.Parse("2022-02-27T00:05:21"));
                using IScoped<ISearchService> searchService = _searchServiceFactory();
                var resourceTypeId = await searchService.Value.GetResourceTypeId(resourceType, cancellationToken);
                var ranges = (await searchService.Value.GetSurrogateIdRanges(resourceTypeId, startId, endId, (int)_exportJobConfiguration.RollingFileSizeInMB * 1024, cancellationToken)).ToList(); // Resources are on average 1kb
                Console.WriteLine($"ExportNoQueue.{_exportJobRecord.ResourceType}: ranges={ranges.Count}.");
                foreach (var range in ranges)
                {
                    await Export(resourceTypeId, range.StartId, range.EndId, cancellationToken);
                    if (_swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                    {
                        lock (_swReport)
                        {
                            if (_swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                            {
                                Console.WriteLine($"ExportNoQueue.{resourceType}.threads={_threads}.Writes={_writesEnabled}.Decompress={_decompressEnabled}.Reads={_readsEnabled}: Resources={_resourcesTotal} secs={(int)_sw.Elapsed.TotalSeconds} speed={(int)(_resourcesTotal / _sw.Elapsed.TotalSeconds)} resources/sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");
                                _swReport.Restart();
                            }
                        }
                    }
                }

                Console.WriteLine($"ExportNoQueue.{resourceType}.threads={_threads}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, resources={_resourcesTotal} speed={_resourcesTotal / _sw.Elapsed.TotalSeconds:N0} resources/sec elapsed={_sw.Elapsed.TotalSeconds:N0} sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");

                await CompleteJobAsync(OperationStatus.Completed, cancellationToken);

                _logger.LogTrace("Successfully completed the job.");
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
            catch (Exception ex)
            {
                // The job has encountered an error it cannot recover from.
                // Try to update the job to failed state.
                _logger.LogError(ex, "Encountered an unhandled exception. The job will be marked as failed.");

                _exportJobRecord.FailureDetails = new JobFailureDetails(Core.Resources.UnknownError, HttpStatusCode.InternalServerError);
                await CompleteJobAsync(OperationStatus.Failed, cancellationToken);
            }
        }

        private async Task CompleteJobAsync(OperationStatus completionStatus, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = completionStatus;
            _exportJobRecord.EndTime = Clock.UtcNow;

            await UpdateJobRecordAsync(cancellationToken);

            await _mediator.Publish(new ExportTaskMetricsNotification(_exportJobRecord), CancellationToken.None);
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

        private async Task<int> Export(short resourceTypeId, long minId, long maxId, CancellationToken cancellationToken)
        {
            if (!_readsEnabled)
            {
                return 0;
            }

            List<byte[]> resources;
            _database.Start();
            using IScoped<ISearchService> searchService = _searchServiceFactory();
            resources = (await searchService.Value.GetDataBytes(resourceTypeId, minId, maxId, cancellationToken)).ToList(); // ToList will fource reading from SQL even when writes are disabled
            _database.Stop();

            var strings = new List<string>();
            if (_decompressEnabled)
            {
                _unzip.Start();
                foreach (var res in resources)
                {
                    using var mem = new MemoryStream(res);
                    strings.Add(await _compressedRawResourceConverter.ReadCompressedRawResource(mem));
                }

                _unzip.Stop();
            }

            if (_writesEnabled)
            {
                _blob.Start();
                WriteBatchOfLines(strings, $"{_exportJobRecord.ResourceType}-{minId}-{maxId}.ndjson");
                _blob.Stop();
            }

            Interlocked.Add(ref _resourcesTotal, strings.Count);

            return strings.Count;
        }

        private void WriteBatchOfLines(IEnumerable<string> batch, string blobName)
        {
            foreach (var resource in batch)
            {
                _exportDestinationClient.WriteFilePart(blobName, resource);
            }

            _exportDestinationClient.CommitFile(blobName);
        }

        private static long LastUpdatedToResourceSurrogateId(DateTime dateTime)
        {
            long id = dateTime.Ticks << 3;

            Debug.Assert(id >= 0, "The ID should not have become negative");
            return id;
        }

        private static DateTime ResourceSurrogateIdToLastUpdated(long resourceSurrogateId)
        {
            var dateTime = new DateTime(resourceSurrogateId >> 3, DateTimeKind.Utc);
            return dateTime;
        }
    }
}
