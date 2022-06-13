// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Data;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed
{
    /// <summary>
    /// A data store that provides functionalities to get resource changes for change feed sources.
    /// </summary>
    public class SqlServerFhirResourceChangeDataStore : IChangeFeedSource<ResourceChangeData>
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirResourceChangeDataStore> _logger;
        private static readonly ConcurrentDictionary<short, string> ResourceTypeIdToTypeNameMap = new ConcurrentDictionary<short, string>();

        // Partition anchor DateTime can be any past DateTime that is not in the retention period.
        // So, January 1st, 1970 at 00:00:00 UTC is chosen as the initial partition anchor DateTime in the resource change data partition function.
        private static readonly DateTime PartitionAnchorDateTime = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Utc);
        private readonly SchemaInformation _schemaInformation;

        /// <summary>
        /// Creates a new instance of the <see cref="SqlServerFhirResourceChangeDataStore"/> class.
        /// </summary>
        /// <param name="sqlConnectionWrapperFactory">The SQL Connection wrapper factory.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="schemaInformation">The database schema information.</param>
        public SqlServerFhirResourceChangeDataStore(SqlConnectionWrapperFactory sqlConnectionWrapperFactory, ILogger<SqlServerFhirResourceChangeDataStore> logger, SchemaInformation schemaInformation)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
            _schemaInformation = schemaInformation;
        }

        /// <summary>
        ///  Returns the number of resource change records from startId.
        /// </summary>
        /// <param name="startId">The start id of resource change records to fetch. The start id is inclusive.</param>
        /// <param name="pageSize">The page size for fetching resource change records.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Resource change data rows.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if startId or pageSize is less than zero.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown when a method call is invalid for the object's current state.</exception>
        /// <exception cref="System.OperationCanceledException">Thrown when the operation is canceled.</exception>
        /// <exception cref="System.Threading.Tasks.TaskCanceledException">Thrown when the task is canceled.</exception>
        /// <exception cref="Microsoft.Data.SqlClient.SqlException">Thrown when SQL Server returns a warning or error.</exception>
        /// <exception cref="System.TimeoutException">Thrown when the time allotted for a process or operation has expired.</exception>
        /// <exception cref="System.Exception">Thrown when errors occur during execution.</exception>
        public async Task<IReadOnlyCollection<ResourceChangeData>> GetRecordsAsync(long startId, short pageSize, CancellationToken cancellationToken)
        {
            return await GetRecordsAsync(startId, PartitionAnchorDateTime, pageSize, cancellationToken);
        }

        /// <summary>
        ///  Returns the number of resource change records from a start id and a checkpoint datetime.
        /// </summary>
        /// <param name="startId">The start id of resource change records to fetch. The start id is inclusive.</param>
        /// <param name="lastProcessedDateTime">The last checkpoint datetime.</param>
        /// <param name="pageSize">The page size for fetching resource change records.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Resource change data rows.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if startId or pageSize is less than zero.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown when a method call is invalid for the object's current state.</exception>
        /// <exception cref="System.OperationCanceledException">Thrown when the operation is canceled.</exception>
        /// <exception cref="System.Threading.Tasks.TaskCanceledException">Thrown when the task is canceled.</exception>
        /// <exception cref="Microsoft.Data.SqlClient.SqlException">Thrown when SQL Server returns a warning or error.</exception>
        /// <exception cref="System.TimeoutException">Thrown when the time allotted for a process or operation has expired.</exception>
        /// <exception cref="System.Exception">Thrown when errors occur during execution.</exception>
        public async Task<IReadOnlyCollection<ResourceChangeData>> GetRecordsAsync(long startId, DateTime lastProcessedDateTime, short pageSize, CancellationToken cancellationToken)
        {
            EnsureArg.IsGte(startId, 1, nameof(startId));
            EnsureArg.IsGte(pageSize, 1, nameof(pageSize));

            var listResourceChangeData = new List<ResourceChangeData>();
            try
            {
                // The GetRecordsAsync function would be called every second by one agent.
                // So, it would be a good option that opens and closes a connection for each call,
                // and there is no database connection pooling in the Application at this time.
                using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
                using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
                {
                    if (ResourceTypeIdToTypeNameMap.IsEmpty)
                    {
                        lock (ResourceTypeIdToTypeNameMap)
                        {
                            if (ResourceTypeIdToTypeNameMap.IsEmpty)
                            {
                                UpdateResourceTypeMapAsync(sqlCommandWrapper);
                            }
                        }
                    }

                    PopulateFetchResourceChangesCommand(sqlCommandWrapper, startId, lastProcessedDateTime, pageSize);

                    using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        while (await sqlDataReader.ReadAsync(cancellationToken))
                        {
                            (long id, DateTime timestamp, string resourceId, short resourceTypeId, int resourceVersion, byte resourceChangeTypeId) = sqlDataReader.ReadRow(
                                    VLatest.ResourceChangeData.Id,
                                    VLatest.ResourceChangeData.Timestamp,
                                    VLatest.ResourceChangeData.ResourceId,
                                    VLatest.ResourceChangeData.ResourceTypeId,
                                    VLatest.ResourceChangeData.ResourceVersion,
                                    VLatest.ResourceChangeData.ResourceChangeTypeId);

                            listResourceChangeData.Add(new ResourceChangeData(
                                id: id,
                                timestamp: DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
                                resourceId: resourceId,
                                resourceTypeId: resourceTypeId,
                                resourceVersion: resourceVersion,
                                resourceChangeTypeId: resourceChangeTypeId,
                                resourceTypeName: ResourceTypeIdToTypeNameMap[resourceTypeId]));
                        }
                    }

                    return listResourceChangeData;
                }
            }
            catch (Exception ex) when ((ex is OperationCanceledException || ex is TaskCanceledException) && cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(ex, Resources.GetRecordsAsyncOperationIsCanceled);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, Resources.ExceptionOccurredWhenFetchingResourceChanges);
                throw;
            }
        }

        private void PopulateFetchResourceChangesCommand(SqlCommandWrapper sqlCommandWrapper, long startId, DateTime lastProcessedDateTime, short pageSize)
        {
            sqlCommandWrapper.CommandType = CommandType.StoredProcedure;
            sqlCommandWrapper.Parameters.AddWithValue("@startId", SqlDbType.BigInt).Value = startId;
            sqlCommandWrapper.Parameters.AddWithValue("@pageSize", SqlDbType.SmallInt).Value = pageSize;
            if (_schemaInformation.Current >= SchemaVersionConstants.SupportsClusteredIdOnResourceChangesVersion)
            {
                sqlCommandWrapper.CommandText = "dbo.FetchResourceChanges_3";
                sqlCommandWrapper.Parameters.AddWithValue("@lastProcessedUtcDateTime", SqlDbType.DateTime2).Value = lastProcessedDateTime;
            }
            else if (_schemaInformation.Current >= SchemaVersionConstants.SupportsPartitionedResourceChangeDataVersion)
            {
                sqlCommandWrapper.CommandText = "dbo.FetchResourceChanges_2";
                sqlCommandWrapper.Parameters.AddWithValue("@lastProcessedDateTime", SqlDbType.DateTime2).Value = lastProcessedDateTime;
            }
            else
            {
                sqlCommandWrapper.CommandText = "dbo.FetchResourceChanges";
            }
        }

        private static void UpdateResourceTypeMapAsync(SqlCommandWrapper sqlCommandWrapper)
        {
            sqlCommandWrapper.CommandText = "SELECT ResourceTypeId, Name FROM dbo.ResourceType";
            using (SqlDataReader sqlDataReader = sqlCommandWrapper.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                while (sqlDataReader.Read())
                {
                    ResourceTypeIdToTypeNameMap.TryAdd((short)sqlDataReader["ResourceTypeId"], (string)sqlDataReader["Name"]);
                }
            }
        }
    }
}
