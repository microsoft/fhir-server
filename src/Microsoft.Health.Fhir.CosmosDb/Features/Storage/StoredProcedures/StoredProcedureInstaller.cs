// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures
{
    public class StoredProcedureInstaller : ICollectionUpdater
    {
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly IEnumerable<IStoredProcedure> _storedProcedures;
        private readonly ILogger<StoredProcedureInstaller> _logger;

        public StoredProcedureInstaller(CosmosDataStoreConfiguration configuration, IEnumerable<IStoredProcedure> storedProcedures, ILogger<StoredProcedureInstaller> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(storedProcedures, nameof(storedProcedures));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _storedProcedures = storedProcedures;
            _logger = logger;
        }

        public async Task ExecuteAsync(Container container, CancellationToken cancellationToken)
        {
            if (_configuration.UseManagedIdentity)
            {
                // Managed Identity does not support read/write stored procedures
                _logger.LogInformation("Skipping stored procedure installation because managed identity is enabled");
                return;
            }

            foreach (IStoredProcedure storedProc in _storedProcedures)
            {
                try
                {
                    await container.Scripts.ReadStoredProcedureAsync(storedProc.FullName, cancellationToken: cancellationToken);
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    await container.Scripts.CreateStoredProcedureAsync(storedProc.ToStoredProcedureProperties(), cancellationToken: cancellationToken);
                }
            }
        }
    }
}
