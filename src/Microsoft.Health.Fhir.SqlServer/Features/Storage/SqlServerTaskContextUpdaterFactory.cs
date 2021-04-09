// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerTaskContextUpdaterFactory : IContextUpdaterFactory
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ILoggerFactory _loggerFactory;

        public SqlServerTaskContextUpdaterFactory(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILoggerFactory loggerFactory)
        {
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _loggerFactory = loggerFactory;
        }

        public IContextUpdater CreateContextUpdater(string taskId, string runId)
        {
            return new SqlServerTaskContextUpdater(taskId, runId, _sqlConnectionWrapperFactory, _loggerFactory.CreateLogger<SqlServerTaskContextUpdater>());
        }
    }
}
