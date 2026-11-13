// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Containers.ContainerRegistry;
using Azure.Identity;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    internal sealed class ContainerRegistryTemplateUploader
    {
        private readonly ContainerRegistryContentClient _client;

        public ContainerRegistryTemplateUploader(ContainerRegistryContentClient client, string registryServer)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(registryServer);

            _client = client;
            RegistryServer = registryServer;
        }

        public string RegistryServer { get; }

        public static ContainerRegistryTemplateUploader CreateFromEnvironment(string repository, Func<string, string> getEnvironmentVariable = null)
        {
            getEnvironmentVariable ??= name => EnvironmentVariables.GetEnvironmentVariable(name);

            string registryServer = GetRequiredVariable(getEnvironmentVariable, KnownEnvironmentVariableNames.TestContainerRegistryServer);
            string tenantId = GetRequiredVariable(getEnvironmentVariable, KnownEnvironmentVariableNames.AzureSubscriptionTenantId);
            string clientId = GetRequiredVariable(getEnvironmentVariable, KnownEnvironmentVariableNames.AzureSubscriptionClientId);
            string serviceConnectionId = GetRequiredVariable(getEnvironmentVariable, KnownEnvironmentVariableNames.AzureSubscriptionServiceConnectionId);
            string systemAccessToken = GetRequiredVariable(getEnvironmentVariable, KnownEnvironmentVariableNames.SystemAccessToken);

            var credential = new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
            var client = new ContainerRegistryContentClient(new Uri($"https://{registryServer}"), repository, credential);

            return new ContainerRegistryTemplateUploader(client, registryServer);
        }

        public async Task UploadTemplateSetAsync(Stream templateLayer, string tag, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(templateLayer);
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);

            // Upload empty config blob
            using var configStream = new MemoryStream("{}"u8.ToArray());
            var configResult = await _client.UploadBlobAsync(configStream, cancellationToken);
            string configDigest = configResult.Value.Digest;
            long configSize = configResult.Value.SizeInBytes;

            // Upload template layer blob — reset seekable streams to avoid silently truncated artifacts
            if (templateLayer.CanSeek)
            {
                templateLayer.Position = 0;
            }

            var layerResult = await _client.UploadBlobAsync(templateLayer, cancellationToken);
            string layerDigest = layerResult.Value.Digest;
            long layerSize = layerResult.Value.SizeInBytes;

            // Build and set manifest
            BinaryData manifest = CreateManifest(configDigest, configSize, layerDigest, layerSize);
            await _client.SetManifestAsync(manifest, tag, ManifestMediaType.OciImageManifest, cancellationToken);
        }

        internal static BinaryData CreateManifest(string configDigest, long configSize, string layerDigest, long layerSize)
        {
            var manifest = new
            {
                schemaVersion = 2,
                config = new
                {
                    mediaType = "application/vnd.oci.image.config.v1+json",
                    digest = configDigest,
                    size = configSize,
                },
                layers = new[]
                {
                    new
                    {
                        mediaType = "application/vnd.oci.image.layer.v1.tar",
                        digest = layerDigest,
                        size = layerSize,
                    },
                },
            };

            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = false });
            return BinaryData.FromString(json);
        }

        private static string GetRequiredVariable(Func<string, string> getEnvironmentVariable, string variableName)
        {
            string value = getEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Required environment variable '{variableName}' is not set or is empty.");
            }

            return value;
        }
    }
}
