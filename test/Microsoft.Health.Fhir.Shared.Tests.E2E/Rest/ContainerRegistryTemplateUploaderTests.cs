// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Containers.ContainerRegistry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomConvertData)]
    public sealed class ContainerRegistryTemplateUploaderTests
    {
        public static IEnumerable<object[]> RequiredVariables =>
            new List<object[]>
            {
                new object[] { KnownEnvironmentVariableNames.TestContainerRegistryServer },
                new object[] { KnownEnvironmentVariableNames.AzureSubscriptionTenantId },
                new object[] { KnownEnvironmentVariableNames.AzureSubscriptionClientId },
                new object[] { KnownEnvironmentVariableNames.AzureSubscriptionServiceConnectionId },
                new object[] { KnownEnvironmentVariableNames.SystemAccessToken },
            };

        [Theory]
        [MemberData(nameof(RequiredVariables))]
        public void CreateFromEnvironment_WhenRequiredVariableIsMissing_ThrowsInvalidOperationException(string missingVariable)
        {
            Func<string, string> getEnvVar = name =>
                name == missingVariable ? string.Empty : "valid-value";

            var ex = Assert.Throws<InvalidOperationException>(
                () => ContainerRegistryTemplateUploader.CreateFromEnvironment("test-repo", getEnvVar));

            Assert.Contains(missingVariable, ex.Message);
        }

        [Fact]
        public void CreateManifest_ReturnsValidOciManifestJson()
        {
            BinaryData result = ContainerRegistryTemplateUploader.CreateManifest("sha256:abc", 10L, "sha256:def", 500L);

            using JsonDocument doc = JsonDocument.Parse(result.ToString());
            JsonElement root = doc.RootElement;

            Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("application/vnd.oci.image.config.v1+json", root.GetProperty("config").GetProperty("mediaType").GetString());
            Assert.Equal("sha256:abc", root.GetProperty("config").GetProperty("digest").GetString());
            Assert.Equal(10, root.GetProperty("config").GetProperty("size").GetInt64());
            Assert.Equal("application/vnd.oci.image.layer.v1.tar", root.GetProperty("layers")[0].GetProperty("mediaType").GetString());
            Assert.Equal("sha256:def", root.GetProperty("layers")[0].GetProperty("digest").GetString());
            Assert.Equal(500, root.GetProperty("layers")[0].GetProperty("size").GetInt64());
        }

        [Fact]
        public void CreateManifest_ReturnsExactlyOneLayer()
        {
            BinaryData result = ContainerRegistryTemplateUploader.CreateManifest("sha256:abc", 10L, "sha256:def", 500L);

            using JsonDocument doc = JsonDocument.Parse(result.ToString());
            JsonElement layers = doc.RootElement.GetProperty("layers");

            Assert.Equal(1, layers.GetArrayLength());
        }

        [Fact]
        public async Task UploadTemplateSetAsync_WhenStreamIsNull_ThrowsArgumentNullException()
        {
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "test.azurecr.io");

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => uploader.UploadTemplateSetAsync(null, "v1"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task UploadTemplateSetAsync_WhenTagIsNullOrEmpty_ThrowsArgumentException(string tag)
        {
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "test.azurecr.io");
            using var stream = new MemoryStream();

            await Assert.ThrowsAsync<ArgumentException>(
                () => uploader.UploadTemplateSetAsync(stream, tag));
        }

        [Fact]
        public async Task UploadTemplateSetAsync_WhenTagIsNull_ThrowsArgumentNullException()
        {
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "test.azurecr.io");
            using var stream = new MemoryStream();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => uploader.UploadTemplateSetAsync(stream, null));
        }

        [Fact]
        public void RegistryServer_ReturnsValuePassedToConstructor()
        {
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "myregistry.azurecr.io");

            Assert.Equal("myregistry.azurecr.io", uploader.RegistryServer);
        }

        [Fact]
        public async Task UploadTemplateSetAsync_WhenSeekableStreamPositionedAtEnd_ResetsPositionToZeroBeforeUpload()
        {
            // Arrange
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "test.azurecr.io");

            byte[] data = [1, 2, 3, 4, 5];
            using var stream = new MemoryStream(data);
            stream.Position = data.Length; // deliberately positioned at end

            long capturedLayerPosition = -1;
            int uploadCallCount = 0;

            mockClient.UploadBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    uploadCallCount++;
                    if (uploadCallCount == 2) // second call is the template layer
                    {
                        capturedLayerPosition = callInfo.Arg<Stream>().Position;
                    }

                    var result = ContainerRegistryModelFactory.UploadRegistryBlobResult("sha256:test", data.Length);
                    return Task.FromResult(Response.FromValue(result, Substitute.For<Response>()));
                });

            // Act
            await uploader.UploadTemplateSetAsync(stream, "v1");

            // Assert
            Assert.Equal(0L, capturedLayerPosition);
        }

        [Fact]
        public async Task UploadTemplateSetAsync_PassesUploadResultDigestsAndSizesToSetManifest()
        {
            // Arrange
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "test.azurecr.io");

            int uploadCallCount = 0;
            mockClient.UploadBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    uploadCallCount++;
                    string digest = uploadCallCount == 1 ? "sha256:configdigest" : "sha256:layerdigest";
                    long size = uploadCallCount == 1 ? 2L : 500L;
                    var result = ContainerRegistryModelFactory.UploadRegistryBlobResult(digest, size);
                    return Task.FromResult(Response.FromValue(result, Substitute.For<Response>()));
                });

            BinaryData capturedManifest = null;
            mockClient.SetManifestAsync(Arg.Any<BinaryData>(), Arg.Any<string>(), Arg.Any<ManifestMediaType?>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedManifest = callInfo.Arg<BinaryData>();
                    var setResult = ContainerRegistryModelFactory.SetManifestResult("sha256:manifest");
                    return Task.FromResult(Response.FromValue(setResult, Substitute.For<Response>()));
                });

            // Act
            using var stream = new MemoryStream([1, 2, 3]);
            await uploader.UploadTemplateSetAsync(stream, "v1");

            // Assert
            Assert.NotNull(capturedManifest);
            using JsonDocument doc = JsonDocument.Parse(capturedManifest.ToString());
            JsonElement root = doc.RootElement;
            Assert.Equal("sha256:configdigest", root.GetProperty("config").GetProperty("digest").GetString());
            Assert.Equal(2L, root.GetProperty("config").GetProperty("size").GetInt64());
            Assert.Equal("sha256:layerdigest", root.GetProperty("layers")[0].GetProperty("digest").GetString());
            Assert.Equal(500L, root.GetProperty("layers")[0].GetProperty("size").GetInt64());
        }
    }
}
