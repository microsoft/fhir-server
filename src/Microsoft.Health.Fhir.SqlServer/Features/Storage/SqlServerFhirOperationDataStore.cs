// -------------------------------------------------------------------------------------------------
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
    internal class SqlServerFhirOperationDataStore : FhirOperationDataStoreBase
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly IQueueClient _queueClient;

        public SqlServerFhirOperationDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            IQueueClient queueClient,
            ILogger<SqlServerFhirOperationDataStore> logger,
            ILoggerFactory loggerFactory)
            : base(queueClient, loggerFactory)
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

        public override async Task<ReindexJobWrapper> CreateReindexJobAsync(ReindexJobRecord jobRecord, CancellationToken cancellationToken)
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

        public override async Task<ReindexJobWrapper> GetReindexJobByIdAsync(string id, CancellationToken cancellationToken)
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

        public override async Task<ReindexJobWrapper> UpdateReindexJobAsync(ReindexJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
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

        public override async Task<IReadOnlyCollection<ReindexJobWrapper>> AcquireReindexJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
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

        public override async Task<(bool found, string id)> CheckActiveReindexJobsAsync(CancellationToken cancellationToken)
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
