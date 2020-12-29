﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ContainerRegistry;
using Microsoft.Azure.ContainerRegistry.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.TemplateManagement.Client;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;

namespace Microsoft.Health.Fhir.Azure
{
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public class ExportDestinationArtifactAcrProvider : IArtifactProvider
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private IAzureContainerRegistryClient _client;
        private ImageInfo _imageInfo;

        public ExportDestinationArtifactAcrProvider()
        {
        }

        public async Task FetchAsync(string blobNameWithETag, Stream targetStream, CancellationToken cancellationToken)
        {
            string registryServer = Environment.GetEnvironmentVariable("TestContainerRegistryServer");
            string registryPassword = Environment.GetEnvironmentVariable("TestContainerRegistryPassword");
            string registryUsername = registryServer.Split('.')[0];

            string imageReference = string.Format("{0}/{1}:{2}", registryServer, "acrtest", "onelayer");
            _imageInfo = ImageInfo.CreateFromImageReference(imageReference);
            string token = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{registryUsername}:{registryPassword}"));

            _client = new AzureContainerRegistryClient(_imageInfo.Registry, new ACRClientCredentials(token));

            // Pull
            Stream rawStream = await Pull(cancellationToken);
            rawStream.CopyTo(targetStream);
        }

        private async Task<Stream> Pull(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await GetManifestAsync(_imageInfo, cancellationToken);

            // Should be only 1 layer refer to the Anonymization config file
            var layer = manifest.Layers[0];
            cancellationToken.ThrowIfCancellationRequested();
            Stream rawStream = await _client.Blob.GetAsync(_imageInfo.ImageName, layer.Digest, cancellationToken);

            // Validate according to layer digest
            /*
                using var streamReader = new MemoryStream();
                rawStream.CopyTo(streamReader);
                var rawBytes = streamReader.ToArray();
                ValidationUtility.ValidateOneBlob(rawBytes, layerDigest);

                OCIArtifactLayer artifactsLayer = new OCIArtifactLayer()
                {
                    Content = rawBytes,
                    Digest = layerDigest,
                    Size = rawBytes.Length,
                };

                StreamReader reader = new StreamReader(new MemoryStream(artifactsLayer.Content));
                string result = await reader.ReadToEndAsync();
            */

            return rawStream;
        }

        public virtual async Task<ManifestWrapper> GetManifestAsync(ImageInfo imageInfo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string mediatypeV2Manifest = "application/vnd.docker.distribution.manifest.v2+json";

            var manifestInfo = await _client.Manifests.GetAsync(imageInfo.ImageName, imageInfo.Label, mediatypeV2Manifest, cancellationToken);

            ValidationUtility.ValidateManifest(manifestInfo);
            return manifestInfo;
        }
    }
}
