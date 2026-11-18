// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    /// <summary>
    /// SQL Server implementation of the job status service.
    /// </summary>
    public class SqlJobStatusService : IJobStatusService
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly IUrlResolver _urlResolver;
        private readonly SchemaInformation _schemaInformation;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlJobStatusService"/> class.
        /// </summary>
        /// <param name="sqlConnectionWrapperFactory">The SQL connection wrapper factory.</param>
        /// <param name="urlResolver">The URL resolver.</param>
        /// <param name="schemaInformation">The current SQL schema information.</param>
        public SqlJobStatusService(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            IUrlResolver urlResolver,
            SchemaInformation schemaInformation)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _urlResolver = EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<JobStatusInfo>> GetAllJobStatusAsync(QueueType? queueType, int count, long? afterGroupId, QueueType? afterQueueType, CancellationToken cancellationToken)
        {
            if (_schemaInformation.Current is not >= SchemaVersionConstants.JobStatus)
            {
                throw new OperationNotImplementedException($"Job status listing requires SQL schema version {SchemaVersionConstants.JobStatus} or later.");
            }

            var jobs = new List<JobStatusInfo>();
            using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
            using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

            sqlCommandWrapper.CommandType = CommandType.StoredProcedure;
            sqlCommandWrapper.CommandText = "dbo.GetJobStatus";
            sqlCommandWrapper.Parameters.Add("@QueueType", SqlDbType.TinyInt).Value = (object)(byte?)queueType ?? DBNull.Value;
            sqlCommandWrapper.Parameters.Add("@PageSize", SqlDbType.Int).Value = count;
            sqlCommandWrapper.Parameters.Add("@AfterGroupId", SqlDbType.BigInt).Value = (object)afterGroupId ?? DBNull.Value;
            sqlCommandWrapper.Parameters.Add("@AfterQueueType", SqlDbType.TinyInt).Value = (object)(byte?)afterQueueType ?? DBNull.Value;

            using SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var groupId = reader.GetInt64(0);
                var jobQueueType = (QueueType)reader.GetByte(1);
                var operationName = GetOperationNameForQueueType(jobQueueType);
                jobs.Add(new JobStatusInfo
                {
                    GroupId = groupId,
                    QueueType = jobQueueType,
                    JobType = operationName,
                    Status = (JobStatus)reader.GetByte(2),
                    ContentLocation = _urlResolver.ResolveOperationResultUrl(operationName, groupId.ToString(CultureInfo.InvariantCulture)),
                    CreateDate = reader.GetDateTime(3),
                    EndDate = await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetDateTime(4),
                });
            }

            return jobs;
        }

        private static string GetOperationNameForQueueType(QueueType queueType)
        {
            return queueType switch
            {
                QueueType.Export => OperationsConstants.Export,
                QueueType.Import => OperationsConstants.Import,
                QueueType.Reindex => OperationsConstants.Reindex,
                QueueType.BulkDelete => OperationsConstants.BulkDelete,
                QueueType.BulkUpdate => OperationsConstants.BulkUpdate,
                _ => throw new ArgumentOutOfRangeException(nameof(queueType)),
            };
        }
    }
}
