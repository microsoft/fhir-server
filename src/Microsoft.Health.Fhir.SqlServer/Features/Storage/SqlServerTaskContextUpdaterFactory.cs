// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.TaskManagement;

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
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _loggerFactory = loggerFactory;
        }

        public IContextUpdater CreateContextUpdater(string taskId, string runId)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(taskId, nameof(taskId));
            EnsureArg.IsNotEmptyOrWhiteSpace(runId, nameof(runId));

            return new SqlServerTaskContextUpdater(taskId, runId, _sqlConnectionWrapperFactory, _loggerFactory.CreateLogger<SqlServerTaskContextUpdater>());
        }
    }
}
