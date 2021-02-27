// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureAccessTokenClientInitializer : IExportClientInitializer<BlobServiceClient>
    {
        private readonly TokenCredential _credential;
        private readonly ExportJobConfiguration _exportJobConfiguration;

        public AzureAccessTokenClientInitializer(
            TokenCredential credential,
            IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            EnsureArg.IsNotNull(credential, nameof(credential));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _credential = credential;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        public async Task<BlobServiceClient> GetAuthorizedClientAsync(CancellationToken cancellationToken)
        {
            return await GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken)
        {
            // Get storage uri from config
            if (string.IsNullOrWhiteSpace(exportJobConfiguration.StorageAccountUri))
            {
                throw new ExportClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            if (!Uri.TryCreate(exportJobConfiguration.StorageAccountUri, UriKind.Absolute, out Uri storageAccountUri))
            {
                throw new ExportClientInitializerException(Resources.InvalidStorageUri, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(new BlobServiceClient(storageAccountUri, _credential));
        }
    }
}
