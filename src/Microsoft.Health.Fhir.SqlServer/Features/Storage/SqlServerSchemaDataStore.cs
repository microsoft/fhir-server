// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Exceptions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerSchemaDataStore : ISchemaDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerSchemaDataStore> _logger;

        public SqlServerSchemaDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerSchemaDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
        }

        public async Task<GetCompatibilityVersionResponse> GetLatestCompatibleVersionAsync(CancellationToken cancellationToken)
        {
            CompatibleVersions compatibleVersions;
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper())
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.SelectCompatibleSchemaVersions.PopulateCommand(sqlCommand);

                using (var dataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                {
                    if (dataReader.Read())
                    {
                        compatibleVersions = new CompatibleVersions(ConvertToInt(dataReader.GetValue(0)), ConvertToInt(dataReader.GetValue(1)));
                    }
                    else
                    {
                        throw new RecordNotFoundException(Resources.CompatibilityRecordNotFound);
                    }
                }

                return new GetCompatibilityVersionResponse(compatibleVersions);
            }
        }

        private int ConvertToInt(object o)
        {
            if (o == DBNull.Value)
            {
                throw new RecordNotFoundException(Resources.CompatibilityRecordNotFound);
            }
            else
            {
               return Convert.ToInt32(o);
            }
        }
    }
}
