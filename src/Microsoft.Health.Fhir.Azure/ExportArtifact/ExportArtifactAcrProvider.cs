// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.ContainerRegistry.Models;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.ArtifactStore;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.TemplateManagement.ArtifactProviders;
using Microsoft.Health.Fhir.TemplateManagement.Client;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;

namespace Microsoft.Health.Fhir.Azure.ExportArtifact
{
    public class ExportArtifactAcrProvider : IArtifactProvider
    {
        private IExportClientInitializer<ACRClient> _exportClientInitializer;
        private ExportJobConfiguration _exportJobConfiguration;
        private ACRClient _client;
        private ImageInfo _imageInfo;
        private OCIArtifactProvider _artifactProvider;

        public ExportArtifactAcrProvider(
            IExportClientInitializer<ACRClient> exportClientInitializer,
            IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            EnsureArg.IsNotNull(exportClientInitializer, nameof(exportClientInitializer));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _exportClientInitializer = exportClientInitializer;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        public async Task FetchAsync(string imageReference, Stream targetStream, CancellationToken cancellationToken)
        {
            _imageInfo = ImageInfo.CreateFromImageReference(imageReference);

            await ConnectAsync(cancellationToken);
            _artifactProvider = new OCIArtifactProvider(_imageInfo, _client);

            // Pull
            var artifactLayers = await GetOCIArtifactAsync(cancellationToken);

            // Should be only 1 layer refer to the Anonymization config file
            var artifacts = StreamUtility.DecompressTarGzStream(new MemoryStream(artifactLayers[0].Content));
            Stream configContent = new MemoryStream(ParseToConfiguration(artifacts));
            configContent.CopyTo(targetStream);
        }

        // To implement another OCIArtifactProvider.GetOCIArtifactAsync(cancellationToken) that can check image size by validating manifest;
        private async Task<List<OCIArtifactLayer>> GetOCIArtifactAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifactsResult = new List<OCIArtifactLayer>();

            // Get Manifest
            ManifestWrapper manifest = await _artifactProvider.GetManifestAsync(cancellationToken);
            ValidateImageSize(manifest, _exportJobConfiguration.MaximumConfigSize);

            // Get Layers
            var layersInfo = manifest.Layers;
            foreach (var layer in layersInfo)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var artifactLayer = await _artifactProvider.GetLayerAsync(layer.Digest, cancellationToken);
                artifactsResult.Add(artifactLayer);
            }

            return artifactsResult;
        }

        public static byte[] ParseToConfiguration(Dictionary<string, byte[]> content)
        {
            try
            {
                // fileContent:
                // <"{folder}", "">
                // <"{folder}/configuration.json", "{....}">
                Regex formatRegex = new Regex(@"(\\|/)_?");
                var fileContent = content.ToDictionary(
                    item =>
                    {
                        var separator = "/";
                        var formattedEntryKeySplit = formatRegex.Replace(item.Key, separator).Split(separator);
                        return string.Join(separator, formattedEntryKeySplit, 1, formattedEntryKeySplit.Length - 1);
                    },
                    item => item.Value == null ? null : item.Value);

                string configurationKey = "configuration.json";
                return fileContent[configurationKey];
            }
            catch (Exception ex)
            {
                throw new TemplateParseException(TemplateManagementErrorCode.ParseTemplatesFailed, "Parse configuration from image failed.", ex);
            }
        }

        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (_client == null)
            {
                _client = await _exportClientInitializer.GetAuthorizedClientAsync(_exportJobConfiguration, cancellationToken);
            }
        }

        private static void ValidateImageSize(ManifestWrapper manifestInfo, int configSizeLimitMegabytes)
        {
            long imageSize = 0;
            foreach (var oneLayer in manifestInfo.Layers)
            {
                imageSize += (long)oneLayer.Size;
            }

            if (imageSize / 1024f / 1024f > configSizeLimitMegabytes)
            {
                throw new ImageTooLargeException(TemplateManagementErrorCode.ImageSizeTooLarge, $"Image size is larger than the size limitation: {configSizeLimitMegabytes} Megabytes");
            }
        }
    }
}
