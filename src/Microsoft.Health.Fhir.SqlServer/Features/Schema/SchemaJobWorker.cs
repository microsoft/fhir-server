// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    /// <summary>
    /// The worker responsible for running the schema job.
    /// </summary>
    public class SchemaJobWorker
    {
        private readonly Func<IScoped<ISchemaDataStore>> _schemaDataStoreFactory;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly ILogger _logger;

        public SchemaJobWorker(Func<IScoped<ISchemaDataStore>> schemaDataStoreFactory, SqlServerDataStoreConfiguration sqlServerDataStoreConfiguration, ILogger<SchemaJobWorker> logger)
        {
            EnsureArg.IsNotNull(schemaDataStoreFactory, nameof(schemaDataStoreFactory));
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _schemaDataStoreFactory = schemaDataStoreFactory;
            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
            _logger = logger;
        }

        public async Task ExecuteAsync(SchemaInformation schemaInformation, string instanceName, CancellationToken cancellationToken)
        {
            using (IScoped<ISchemaDataStore> store = _schemaDataStoreFactory())
            {
                await store.Value.InsertInstanceSchemaInformation(instanceName, schemaInformation, cancellationToken);
            }

            _logger.LogInformation($"Polling started at {Clock.UtcNow}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (IScoped<ISchemaDataStore> store = _schemaDataStoreFactory())
                    {
                        // Ensure schemaInformation has the latest current version
                        schemaInformation.Current = await store.Value.UpsertInstanceSchemaInformation(instanceName, schemaInformation, cancellationToken);

                        await store.Value.DeleteExpiredRecords();
                    }

                    await Task.Delay(_sqlServerDataStoreConfiguration.SchemaUpdatesJobPollingFrequency, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cancel requested.
                }
                catch (Exception ex)
                {
                    // The job failed.
                    _logger.LogError(ex, "Unhandled exception in the worker.");
                }
            }
        }
    }
}
