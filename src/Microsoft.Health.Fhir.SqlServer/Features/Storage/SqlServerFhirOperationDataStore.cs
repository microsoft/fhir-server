// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly JsonSerializerSettings _jsonSerializerSettings;

        public SqlServerFhirOperationDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerFhirOperationDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;

            _jsonSerializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = new List<JsonConverter>
                {
                    new EnumLiteralJsonConverter(),
                },
            };
        }

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                V1.CreateExportJob.PopulateCommand(
                    sqlCommand,
                    jobRecord.Id,
                    jobRecord.Hash,
                    jobRecord.Status.ToString(),
                    jobRecord.QueuedTime,
                    JsonConvert.SerializeObject(jobRecord, _jsonSerializerSettings));

                var rowVersion = (int?)await sqlCommand.ExecuteScalarAsync(cancellationToken);

                if (rowVersion == null)
                {
                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, Resources.NullRowVersion), HttpStatusCode.InternalServerError);
                }

                return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion.ToString()));
            }
        }

        public async Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                V1.GetExportJobById.PopulateCommand(sqlCommand, id);

                using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    if (!sqlDataReader.Read())
                    {
                        throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, id));
                    }

                    (string rawJobRecord, byte[] rowVersion) = sqlDataReader.ReadRow(V1.ExportJob.RawJobRecord, V1.ExportJob.JobVersion);

                    return CreateExportJobOutcome(rawJobRecord, rowVersion);
                }
            }
        }

        public async Task<ExportJobOutcome> GetExportJobByHashAsync(string hash, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(hash, nameof(hash));

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                V1.GetExportJobByHash.PopulateCommand(sqlCommand, hash);

                using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    (string rawJobRecord, byte[] rowVersion) = sqlDataReader.ReadRow(V1.ExportJob.RawJobRecord, V1.ExportJob.JobVersion);

                    return CreateExportJobOutcome(rawJobRecord, rowVersion);
                }
            }
        }

        public async Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            byte[] rowVersionAsBytes = GetRowVersionAsBytes(eTag);

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                V1.UpdateExportJob.PopulateCommand(
                    sqlCommand,
                    jobRecord.Id,
                    jobRecord.Status.ToString(),
                    jobRecord.QueuedTime,
                    JsonConvert.SerializeObject(jobRecord, _jsonSerializerSettings),
                    rowVersionAsBytes);

                try
                {
                    var rowVersion = (int?)await sqlCommand.ExecuteScalarAsync(cancellationToken);

                    if (rowVersion == null)
                    {
                        throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, Resources.NullRowVersion), HttpStatusCode.InternalServerError);
                    }

                    return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion.ToString()));
                }
                catch (SqlException e)
                {
                    if (e.Number == SqlErrorCodes.PreconditionFailed)
                    {
                        throw new JobConflictException();
                    }
                    else
                    {
                        _logger.LogError(e, Core.Resources.SqlErrorOnExportUpdate);
                        throw;
                    }
                }
            }
        }

        public async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                var jobHeartbeatTimeoutThresholdInSeconds = Convert.ToInt64(jobHeartbeatTimeoutThreshold.TotalSeconds);

                V1.AcquireExportJobs.PopulateCommand(
                    sqlCommand,
                    jobHeartbeatTimeoutThresholdInSeconds,
                    maximumNumberOfConcurrentJobsAllowed);

                var acquiredJobs = new List<ExportJobOutcome>();

                using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        (string rawJobRecord, byte[] rowVersion) = sqlDataReader.ReadRow(V1.ExportJob.RawJobRecord, V1.ExportJob.JobVersion);

                        acquiredJobs.Add(CreateExportJobOutcome(rawJobRecord, rowVersion));
                    }
                }

                return acquiredJobs;
            }
        }

        private static ExportJobOutcome CreateExportJobOutcome(string rawJobRecord, byte[] rowVersionAsBytes)
        {
            var exportJobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord);

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

            BitConverter.TryWriteBytes(versionAsBytes, int.Parse(eTag.VersionId));

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(versionAsBytes);
            }

            return versionAsBytes;
        }
    }
}
