﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly IQueueClient _queueClient;

        public SqlServerFhirOperationDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            IQueueClient queueClient,
            ILogger<SqlServerFhirOperationDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _queueClient = queueClient;
            _logger = logger;

            _jsonSerializerSettings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new EnumLiteralJsonConverter(),
                },
            };
        }

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            var clone = jobRecord.Clone();
            clone.QueuedTime = DateTime.Parse("1900-01-01");
            var def = JsonConvert.SerializeObject(clone, _jsonSerializerSettings);
            var results = await _queueClient.EnqueueAsync((byte)QueueType.Export, new[] { def }, null, false, clone.Status == OperationStatus.Completed, cancellationToken);

            if (results.Count != 1)
            {
                throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, "Failed to create export job."), HttpStatusCode.InternalServerError);
            }

            var jobInfo = results[0];
            return CreateExportJobOutcome(jobInfo, clone);
        }

        public async Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            if (!long.TryParse(id, out var jobId))
            {
                throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
            }

            var jobInfo = await _queueClient.GetJobByIdAsync((byte)QueueType.Export, jobId, true, cancellationToken);

            if (jobInfo == null)
            {
                throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
            }

            var def = jobInfo.Definition;
            var status = jobInfo.Status;
            var result = jobInfo.Result;
            var record = JsonConvert.DeserializeObject<ExportJobRecord>(jobInfo.Definition);

            if (status == JobStatus.Completed)
            {
                var groupJobs = (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, jobInfo.GroupId, false, cancellationToken)).ToList();
                var inFlightJobsExist = groupJobs.Where(_ => _.Id != jobInfo.Id).Any(_ => _.Status == JobStatus.Running || _.Status == JobStatus.Created);
                var cancelledJobsExist = groupJobs.Where(_ => _.Id != jobInfo.Id).Any(_ => _.Status == JobStatus.Cancelled || (_.Status == JobStatus.Running && _.CancelRequested));
                var failedJobsExist = groupJobs.Where(_ => _.Id != jobInfo.Id).Any(_ => _.Status == JobStatus.Failed);

                if (cancelledJobsExist && !failedJobsExist)
                {
                    status = JobStatus.Cancelled;
                }
                else if (failedJobsExist)
                {
                    foreach (var job in groupJobs.Where(_ => _.Id != jobInfo.Id && _.Status == JobStatus.Failed))
                    {
                        if (!string.IsNullOrEmpty(job.Result) && !job.Result.Equals("null", StringComparison.OrdinalIgnoreCase))
                        {
                            var processResult = JsonConvert.DeserializeObject<ExportJobRecord>(job.Result);
                            if (processResult.FailureDetails != null)
                            {
                                if (record.FailureDetails == null)
                                {
                                    record.FailureDetails = processResult.FailureDetails;
                                }
                                else if (!processResult.FailureDetails.FailureReason.Equals(record.FailureDetails.FailureReason, StringComparison.OrdinalIgnoreCase))
                                {
                                    record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\n" + processResult.FailureDetails.FailureReason, record.FailureDetails.FailureStatusCode);
                                }
                            }
                        }
                        else
                        {
                            if (record.FailureDetails == null)
                            {
                                record.FailureDetails = new JobFailureDetails("Processing job had no results", HttpStatusCode.InternalServerError);
                            }
                            else
                            {
                                record.FailureDetails = new JobFailureDetails(record.FailureDetails.FailureReason + "\r\nProcessing job had no results", HttpStatusCode.InternalServerError);
                            }
                        }
                    }

                    record.Status = OperationStatus.Failed;
                    status = JobStatus.Failed;
                    result = JsonConvert.SerializeObject(record);
                }
                else if (!inFlightJobsExist) // no failures here
                {
                    foreach (var job in groupJobs.Where(_ => _.Id != jobInfo.Id))
                    {
                        if (!string.IsNullOrEmpty(job.Result) && !job.Result.Equals("null", StringComparison.OrdinalIgnoreCase))
                        {
                            var processResult = JsonConvert.DeserializeObject<ExportJobRecord>(job.Result);
                            foreach (var output in processResult.Output)
                            {
                                if (record.Output.TryGetValue(output.Key, out var exportFileInfos))
                                {
                                    exportFileInfos.AddRange(output.Value);
                                }
                                else
                                {
                                    record.Output.Add(output.Key, output.Value);
                                }
                            }
                        }
                    }

                    record.Status = OperationStatus.Completed;
                    status = JobStatus.Completed;
                    result = JsonConvert.SerializeObject(record);
                }
                else
                {
                    status = JobStatus.Running;
                }
            }

            return CreateExportJobOutcome(jobId, result ?? def, jobInfo.Version, (byte)status, jobInfo.CreateDate);
        }

        public async Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            if (jobRecord.Status != OperationStatus.Canceled)
            {
                throw new NotSupportedException($"Calls to this method with status={jobRecord.Status} are deprecated.");
            }

            eTag ??= WeakETag.FromVersionId("0");

            try
            {
                var jobWithGroupId = await _queueClient.GetJobByIdAsync((byte)QueueType.Export, long.Parse(jobRecord.Id), false, cancellationToken);
                await _queueClient.CancelJobByGroupIdAsync((byte)QueueType.Export, jobWithGroupId.GroupId, cancellationToken);
            }
            catch (JobNotExistException ex)
            {
                throw new JobNotFoundException(ex.Message);
            }

            return new ExportJobOutcome(jobRecord, eTag);
        }

        public async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort numberOfExportJobsToAcquire, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<JobInfo> jobInfos = await _queueClient.DequeueJobsAsync((byte)QueueType.Export, numberOfExportJobsToAcquire, Environment.MachineName, (int)jobHeartbeatTimeoutThreshold.TotalSeconds, cancellationToken);

            var acquiredJobs = new List<ExportJobOutcome>();

            foreach (var job in jobInfos)
            {
                var id = job.Id;
                var def = job.Definition;
                var version = job.Version;
                var status = job.Status;
                var result = job.Result;
                var createDate = job.CreateDate;
                var rawJobRecord = result ?? def;
                acquiredJobs.Add(CreateExportJobOutcome(id, rawJobRecord, version, (byte)status, createDate));
            }

            return acquiredJobs;
        }

        public async Task<ReindexJobWrapper> CreateReindexJobAsync(ReindexJobRecord jobRecord, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.CreateReindexJob.PopulateCommand(
                    sqlCommandWrapper,
                    jobRecord.Id,
                    jobRecord.Status.ToString(),
                    JsonConvert.SerializeObject(jobRecord, _jsonSerializerSettings));

                var rowVersion = (int?)await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);

                if (rowVersion == null)
                {
                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Reindex, "Failed to create reindex job because no row version was returned."), HttpStatusCode.InternalServerError);
                }

                return new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId(rowVersion.ToString()));
            }
        }

        public async Task<ReindexJobWrapper> GetReindexJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetReindexJobById.PopulateCommand(sqlCommandWrapper, id);

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    if (!await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
                    }

                    (string rawJobRecord, byte[] rowVersion) = sqlDataReader.ReadRow(VLatest.ReindexJob.RawJobRecord, VLatest.ReindexJob.JobVersion);

                    return CreateReindexJobWrapper(rawJobRecord, rowVersion);
                }
            }
        }

        public async Task<ReindexJobWrapper> UpdateReindexJobAsync(ReindexJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            byte[] rowVersionAsBytes = GetRowVersionAsBytes(eTag);

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.UpdateReindexJob.PopulateCommand(
                    sqlCommandWrapper,
                    jobRecord.Id,
                    jobRecord.Status.ToString(),
                    JsonConvert.SerializeObject(jobRecord, _jsonSerializerSettings),
                    rowVersionAsBytes);

                try
                {
                    var rowVersion = (byte[])await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);

                    if (rowVersion.NullIfEmpty() == null)
                    {
                        throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Reindex, "Failed to create reindex job because no row version was returned."), HttpStatusCode.InternalServerError);
                    }

                    return new ReindexJobWrapper(jobRecord, GetRowVersionAsEtag(rowVersion));
                }
                catch (SqlException e)
                {
                    if (e.Number == SqlErrorCodes.PreconditionFailed)
                    {
                        throw new Core.Features.Operations.JobConflictException();
                    }
                    else if (e.Number == SqlErrorCodes.NotFound)
                    {
                        throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, jobRecord.Id));
                    }
                    else
                    {
                        _logger.LogError(e, "Error from SQL database on reindex job update.");
                        throw;
                    }
                }
            }
        }

        public async Task<IReadOnlyCollection<ReindexJobWrapper>> AcquireReindexJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                var jobHeartbeatTimeoutThresholdInSeconds = Convert.ToInt64(jobHeartbeatTimeoutThreshold.TotalSeconds);

                VLatest.AcquireReindexJobs.PopulateCommand(
                    sqlCommandWrapper,
                    jobHeartbeatTimeoutThresholdInSeconds,
                    maximumNumberOfConcurrentJobsAllowed);

                var acquiredJobs = new List<ReindexJobWrapper>();

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        (string rawJobRecord, byte[] rowVersion) = sqlDataReader.ReadRow(VLatest.ReindexJob.RawJobRecord, VLatest.ReindexJob.JobVersion);

                        acquiredJobs.Add(CreateReindexJobWrapper(rawJobRecord, rowVersion));
                    }
                }

                return acquiredJobs;
            }
        }

        public async Task<(bool found, string id)> CheckActiveReindexJobsAsync(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.CheckActiveReindexJobs.PopulateCommand(sqlCommandWrapper);

                var activeJobs = new List<string>();

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        string id = sqlDataReader.ReadRow(VLatest.ReindexJob.Id);

                        activeJobs.Add(id);
                    }
                }

                // Currently, there can only be one active reindex job at a time.
                return (activeJobs.Count > 0, activeJobs.Count > 0 ? activeJobs.FirstOrDefault() : string.Empty);
            }
        }

        private ExportJobOutcome CreateExportJobOutcome(long jobId, string rawJobRecord, long version, byte status, DateTime createDate)
        {
            try
            {
                var exportJobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord, _jsonSerializerSettings);
                return CreateExportJobOutcome(jobId, exportJobRecord, version, status, createDate);
            }
            catch (Exception ex)
            {
                throw new JsonSerializationException($"Error deserializing {rawJobRecord}", ex);
            }
        }

        private static ExportJobOutcome CreateExportJobOutcome(JobInfo jobInfo, ExportJobRecord jobRecord)
        {
            return CreateExportJobOutcome(jobInfo.Id, jobRecord, jobInfo.Version, (byte)jobInfo.Status, jobInfo.CreateDate);
        }

        private static ExportJobOutcome CreateExportJobOutcome(long jobId, ExportJobRecord jobRecord, long version, byte status, DateTime createDate)
        {
            jobRecord.Id = jobId.ToString();
            jobRecord.QueuedTime = createDate;
            jobRecord.Status = (OperationStatus)status;
            var etag = GetRowVersionAsEtag(BitConverter.GetBytes(version));
            return new ExportJobOutcome(jobRecord, etag);
        }

        private ExportJobOutcome CreateExportJobOutcomeToBeDeprecated(string rawJobRecord, byte[] rowVersionAsBytes)
        {
            var exportJobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord, _jsonSerializerSettings);
            WeakETag etag = GetRowVersionAsEtag(rowVersionAsBytes);
            return new ExportJobOutcome(exportJobRecord, etag);
        }

        private static WeakETag GetRowVersionAsEtag(byte[] rowVersionAsBytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(rowVersionAsBytes);
            }

            var rowVersionAsDecimalString = BitConverter.ToInt64(rowVersionAsBytes, startIndex: 0).ToString();

            return WeakETag.FromVersionId(rowVersionAsDecimalString);
        }

        private static byte[] GetRowVersionAsBytes(WeakETag eTag)
        {
            // The SQL rowversion data type is 8 bytes in length.
            var versionAsBytes = new byte[8];

            BitConverter.TryWriteBytes(versionAsBytes, long.Parse(eTag.VersionId));

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(versionAsBytes);
            }

            return versionAsBytes;
        }

        private ReindexJobWrapper CreateReindexJobWrapper(string rawJobRecord, byte[] rowVersionAsBytes)
        {
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(rawJobRecord, _jsonSerializerSettings);

            WeakETag etag = GetRowVersionAsEtag(rowVersionAsBytes);

            return new ReindexJobWrapper(reindexJobRecord, etag);
        }
    }
}
