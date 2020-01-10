// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;

        public SqlServerFhirOperationDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerFhirOperationDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            // We will consider a job to be stale if its timestamp is smaller than or equal to this.
            DateTimeOffset expirationTime = Clock.UtcNow - jobHeartbeatTimeoutThreshold;

            // We will timestamp the jobs when we mark them as running to track stale jobs.
            DateTimeOffset heartbeatTimeStamp = Clock.UtcNow;

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                V1.AcquireExportJobs.PopulateCommand(
                    sqlCommand,
                    expirationTime,
                    maximumNumberOfConcurrentJobsAllowed,
                    heartbeatTimeStamp);

                var acquiredJobs = new List<ExportJobOutcome>();

                using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        (string rawJobRecord, byte[] rowVersionAsBytes) = sqlDataReader.ReadRow(V1.ExportJob.RawJobRecord, V1.ExportJob.JobVersion);

                        var exportJobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord);

                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(rowVersionAsBytes);
                        }

                        const int startIndex = 0;
                        var rowVersionAsDecimalString = BitConverter.ToInt32(rowVersionAsBytes, startIndex).ToString();

                        acquiredJobs.Add(new ExportJobOutcome(exportJobRecord, WeakETag.FromVersionId(rowVersionAsDecimalString)));
                    }
                }

                return new ReadOnlyCollection<ExportJobOutcome>(acquiredJobs);
            }
        }

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                V1.CreateExportJob.PopulateCommand(
                    sqlCommand,
                    jobRecord.Id,
                    jobRecord.Status.ToString(),
                    jobRecord.QueuedTime,
                    JsonConvert.SerializeObject(jobRecord));

                var rowVersion = (int?)await sqlCommand.ExecuteScalarAsync(cancellationToken);

                if (rowVersion == null)
                {
                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, Resources.NullRowVersion), HttpStatusCode.InternalServerError);
                }

                return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion.ToString()));
            }
        }

        public Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ExportJobOutcome> GetExportJobByHashAsync(string hash, CancellationToken cancellationToken)
        {
            // TODO: Implement this method (returning null for now to allow the create method to run).
            return Task.FromResult<ExportJobOutcome>(null);
        }

        public Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            // TODO: Implement this method.
            return Task.FromResult(new ExportJobOutcome(jobRecord, eTag));
        }
    }
}
