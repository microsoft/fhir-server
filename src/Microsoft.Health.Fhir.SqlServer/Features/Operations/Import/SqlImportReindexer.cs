// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlImportReindexer : IImportOrchestratorJobDataStoreOperation
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private ILogger<SqlImportReindexer> _logger;

        public SqlImportReindexer(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImportReindexer> logger)
        {
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _importTaskConfiguration = EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig)).Value.Import;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task PreprocessAsync(CancellationToken cancellationToken)
        {
            if (_importTaskConfiguration.DisableOptionalIndexesForImport)
            {
                try
                {
                    using var sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                    using var sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();
                    sqlCommandWrapper.CommandType = CommandType.StoredProcedure;
                    sqlCommandWrapper.CommandText = "dbo.DisableIndexes";
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "PreprocessAsync failed.");
                    if (ex.IsRetriable())
                    {
                        throw new RetriableJobException(ex.Message, ex);
                    }

                    throw;
                }
            }
        }

        public async Task PostprocessAsync(CancellationToken cancellationToken)
        {
            if (_importTaskConfiguration.DisableOptionalIndexesForImport)
            {
                try
                {
                    await SwitchPartitionsOutAllTables(false, cancellationToken);
                    var indexRebuildCommands = await GetCommandsForRebuildIndexes(false, cancellationToken);
                    await ExecuteCommands(indexRebuildCommands, cancellationToken);
                    await SwitchPartitionsInAllTables(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "PostprocessAsync failed.");
                    if (ex.IsRetriable())
                    {
                        throw new RetriableJobException(ex.Message, ex);
                    }

                    throw;
                }
            }
        }

        private async Task<IList<(string tableName, string indexName, string command)>> GetCommandsForRebuildIndexes(bool rebuildClustered, CancellationToken cancellationToken)
        {
            var indexes = new List<(string tableName, string indexName, string command)>();
            using var sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
            using var sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();
            VLatest.GetCommandsForRebuildIndexes.PopulateCommand(sqlCommandWrapper, rebuildClustered);
            using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
            while (await sqlDataReader.ReadAsync(cancellationToken))
            {
                var tableName = sqlDataReader.GetString(0);
                var indexName = sqlDataReader.GetString(1);
                var command = sqlDataReader.GetString(2);
                indexes.Add((tableName, indexName, command));
            }

            return indexes;
        }

        private async Task ExecuteCommands(IList<(string tableName, string indexName, string command)> commands, CancellationToken cancellationToken)
        {
            try
            {
                var commandQueue = new Queue<(string tableName, string indexName, string command)>();
                foreach (var command in commands)
                {
                    commandQueue.Enqueue(command);
                }

                var tasks = new List<Task>();
                for (var thread = 0; thread < _importTaskConfiguration.SqlIndexRebuildThreads; thread++)
                {
                    tasks.Add(ExecuteCommandsWorker(commandQueue, cancellationToken));
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild indexes");
                if (ex.IsRetriable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        private async Task ExecuteCommandsWorker(Queue<(string tableName, string indexName, string command)> commandQueue, CancellationToken cancellationToken)
        {
            while (commandQueue.Count > 0)
            {
                if (commandQueue.TryDequeue(out var command))
                {
                    await ExecuteCommand(command.tableName, command.indexName, command.command, cancellationToken);
                }
            }
        }

        private async Task ExecuteCommand(string tableName, string indexName, string command, CancellationToken cancellationToken)
        {
            var retries = 0;
            var sw = Stopwatch.StartNew();
            retry:
            _logger.LogInformation(string.Format("started. table: {0}, index: {1}, retries: {2}", tableName, indexName, retries));
            try
            {
                using var sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
                using var sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();
                sqlCommandWrapper.CommandTimeout = _importTaskConfiguration.InfinitySqlTimeoutSec;
                VLatest.ExecuteCommandForRebuildIndexes.PopulateCommand(sqlCommandWrapper, tableName, indexName, command);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException ex)
            {
                if (ex.IsRetriable())
                {
                    _logger.LogWarning(ex, string.Format("failed with retriable error. table: {0}, index: {1}, retries: {2}", tableName, indexName, retries));
                    retries++;
                    goto retry;
                }

                _logger.LogError(ex, string.Format("failed. table: {0}, index: {1}, retries: {2}", tableName, indexName, retries));
                throw;
            }

            _logger.LogInformation(string.Format("completed. table: {0}, index: {1}, retries: {2}, elapsed(sec): {3}", tableName, indexName, retries, (int)sw.Elapsed.TotalSeconds));

            return;
        }

        private async Task SwitchPartitionsOutAllTables(bool rebuildClustered, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.SwitchPartitionsOutAllTables.PopulateCommand(sqlCommandWrapper, rebuildClustered);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task SwitchPartitionsInAllTables(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.SwitchPartitionsInAllTables.PopulateCommand(sqlCommandWrapper);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
