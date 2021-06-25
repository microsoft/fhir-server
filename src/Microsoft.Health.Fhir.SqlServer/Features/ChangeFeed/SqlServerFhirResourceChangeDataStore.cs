// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Data;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed
{
    public class SqlServerFhirResourceChangeDataStore : IChangeFeedSource<IResourceChangeData>
    {
        private readonly ISqlConnectionFactory _sqlConnectionFactory;
        private readonly ILogger<SqlServerFhirResourceChangeDataStore> _logger;

        public SqlServerFhirResourceChangeDataStore(ISqlConnectionFactory sqlConnectionFactory, ILogger<SqlServerFhirResourceChangeDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionFactory, nameof(sqlConnectionFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionFactory = sqlConnectionFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<IResourceChangeData>> GetRecordsAsync(long startId, int pageSize, CancellationToken cancellationToken)
        {
            EnsureArg.IsGte(startId, 0, nameof(startId));
            EnsureArg.IsGte(pageSize, 0, nameof(pageSize));

            var listResourceChangeData = new List<ResourceChangeData>();
            try
            {
                using (SqlConnection sqlConnection = await _sqlConnectionFactory.GetSqlConnectionAsync(cancellationToken: cancellationToken))
                {
                    await sqlConnection.OpenAsync(cancellationToken);
                    using (SqlCommand sqlCommand = new SqlCommand("dbo.FetchResourceChanges", sqlConnection))
                    {
                        sqlCommand.CommandType = CommandType.StoredProcedure;
                        sqlCommand.Parameters.AddWithValue("@startId", SqlDbType.BigInt).Value = startId;
                        sqlCommand.Parameters.AddWithValue("@pageSize", SqlDbType.Int).Value = pageSize;

                        using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                        {
                            while (await sqlDataReader.ReadAsync(cancellationToken))
                            {
                                listResourceChangeData.Add(new ResourceChangeData
                                {
                                    Id = (long)sqlDataReader["Id"],
                                    Timestamp = DateTime.SpecifyKind((DateTime)sqlDataReader["Timestamp"], DateTimeKind.Utc),
                                    ResourceId = (string)sqlDataReader["ResourceId"],
                                    ResourceTypeId = (short)sqlDataReader["ResourceTypeId"],
                                    ResourceVersion = (int)sqlDataReader["ResourceVersion"],
                                    ResourceChangeTypeId = (byte)sqlDataReader["ResourceChangeTypeId"],
                                });
                            }
                        }

                        return listResourceChangeData;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error from SQL database on FetchResourceChangeData");
                throw;
            }
        }
    }
}
