// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    internal class QueryProcessor : IQueryProcessor
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;

        public QueryProcessor(SqlConnectionWrapperFactory sqlConnectionWrapperFactory)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
        }

        public int GetLatestCompatibleVersion(int maxVersion)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.SelectMaxSupportedSchemaVersion.PopulateCommand(sqlCommand, maxVersion);

                object maxSupportedVersion = sqlCommand.ExecuteScalar();
                return (int)maxSupportedVersion;
            }
        }
    }
}
