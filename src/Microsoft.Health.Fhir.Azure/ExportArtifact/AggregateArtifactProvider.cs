// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.ArtifactStore;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.TemplateManagement.Models;

namespace Microsoft.Health.Fhir.Azure.ExportArtifact
{
    public class AggregateArtifactProvider : IArtifactProvider
    {
        private ExportArtifactAcrProvider _exportArtifactAcrProvider;
        private ExportArtifactStorageProvider _exportArtifactStorageProvider;
        private ILogger<ExportJobTask> _logger;

        public AggregateArtifactProvider(
            ExportArtifactAcrProvider exportArtifactAcrProvider,
            ExportArtifactStorageProvider exportArtifactStorageProvider,
            ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportArtifactAcrProvider = exportArtifactAcrProvider;
            _exportArtifactStorageProvider = exportArtifactStorageProvider;
            _logger = logger;
        }

        public async Task FetchAsync(string configurationLocation, Stream targetStream, CancellationToken cancellationToken)
        {
            var type = ImageInfo.IsValidImageReference(configurationLocation) ?
                                    ExportArtifaetStoreConstants.Type.Acr :
                                    ExportArtifaetStoreConstants.Type.Storage;
            _logger.LogInformation($"Resolve artifact type for $Export is: {type}");

            switch (type)
            {
                case ExportArtifaetStoreConstants.Type.Acr:
                    await _exportArtifactAcrProvider.FetchAsync(configurationLocation, targetStream, cancellationToken);
                    break;
                case ExportArtifaetStoreConstants.Type.Storage:
                    await _exportArtifactStorageProvider.FetchAsync(configurationLocation, targetStream, cancellationToken);
                    break;
                default:
                    throw new Exception("Not support artifact store type");
            }
        }
    }
}
