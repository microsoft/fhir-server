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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.TemplateManagement.ArtifactProviders;
using Microsoft.Health.Fhir.TemplateManagement.Client;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;

namespace Microsoft.Health.Fhir.Azure
{
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public class ExportDestinationArtifactAcrProvider : IArtifactProvider
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private IExportClientInitializer<ACRClient> _exportClientInitializer;
        private ExportJobConfiguration _exportJobConfiguration;
        private ACRClient _client;
        private ImageInfo _imageInfo;
        private OCIArtifactProvider _artifactProvider;

        public ExportDestinationArtifactAcrProvider(
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
            string registryServer = _exportJobConfiguration.AcrServer;
            _imageInfo = ImageInfo.CreateFromImageReference(imageReference);

            await ConnectAsync(cancellationToken);
            _artifactProvider = new OCIArtifactProvider(_imageInfo, _client);

            // Pull
            Stream rawStream = await Pull(cancellationToken);
            rawStream.CopyTo(targetStream);
        }

        private async Task<Stream> Pull(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifactLayers = await _artifactProvider.GetOCIArtifactAsync(cancellationToken);

            // Should be only 1 layer refer to the Anonymization config file
            var artifacts = StreamUtility.DecompressTarGzStream(new MemoryStream(artifactLayers[0].Content));
            Stream configContent = new MemoryStream(ParseToConfiguration(artifacts));
            return configContent;
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

        // Should be updated in order to validate image size
        private async Task<ManifestWrapper> GetManifestAsync(CancellationToken cancellationToken = default)
        {
            var manifestInfo = await _artifactProvider.GetManifestAsync(cancellationToken);
            ValidateImageSize(manifestInfo, _exportJobConfiguration.MaximumConfigSize);
            return manifestInfo;
        }

        private async Task<OCIArtifactLayer> GetLayerAsync(string layerDigest, CancellationToken cancellationToken = default)
        {
            var artifactsLayer = await _artifactProvider.GetLayerAsync(layerDigest, cancellationToken);
            return artifactsLayer;
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
