// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportErrorStoreFactory : IImportErrorStoreFactory
    {
        private const string DefaultLogContainerName = "fhirlogs";

        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private ILoggerFactory _loggerFactory;

        public ImportErrorStoreFactory(IIntegrationDataStoreClient integrationDataStoreClient, ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _integrationDataStoreClient = integrationDataStoreClient;
            _loggerFactory = loggerFactory;
        }

        public Task<IImportErrorStore> InitializeAsync(string fileName, CancellationToken cancellationToken)
        {
            return InitializeAsync(DefaultLogContainerName, fileName, cancellationToken);
        }

        public async Task<IImportErrorStore> InitializeAsync(string containerName, string fileName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(containerName, nameof(containerName));
            EnsureArg.IsNotNullOrEmpty(fileName, nameof(fileName));

            Uri fileUri = await _integrationDataStoreClient.PrepareResourceAsync(containerName, fileName, cancellationToken);
            return new ImportErrorStore(_integrationDataStoreClient, fileUri, _loggerFactory.CreateLogger<ImportErrorStore>());
        }
    }
}
