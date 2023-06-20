// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    public class TransactionWatchdog : Watchdog<TransactionWatchdog>
    {
        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _sqlConnectionWrapperFactory;
        private readonly ILogger<TransactionWatchdog> _logger;
        private CancellationToken _cancellationToken;
        private const double _periodSec = 5;
        private const double _leasePeriodSec = 30;

        public TransactionWatchdog(Func<IScoped<SqlConnectionWrapperFactory>> sqlConnectionWrapperFactory, ILogger<TransactionWatchdog> logger)
            : base(sqlConnectionWrapperFactory, logger)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            await StartAsync(true, _periodSec, _leasePeriodSec, cancellationToken);
        }

        protected override async Task ExecuteAsync()
        {
            _logger.LogInformation("TransactionWatchdog starting...");
            using IScoped<SqlConnectionWrapperFactory> scopedConn = _sqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper conn = await scopedConn.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "dbo.MergeResourcesAdvanceTransactionVisibility";
            cmd.CommandType = CommandType.StoredProcedure;
            var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(affectedRowsParam);
            await cmd.ExecuteNonQueryAsync(_cancellationToken);
            var affectedRows = (int)affectedRowsParam.Value;
            _logger.LogInformation("TransactionWatchdog advanced visibility on {Transactions} transactions.", affectedRows);
        }
    }
}
