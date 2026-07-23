// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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

            await Assert.ThrowsAsync<ArgumentException>(
                () => uploader.UploadTemplateSetAsync(new MemoryStream(), tag));
        }

        [Fact]
        public async Task UploadTemplateSetAsync_WhenTagIsNull_ThrowsArgumentNullException()
        {
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "test.azurecr.io");

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => uploader.UploadTemplateSetAsync(new MemoryStream(), null));
        }

        [Fact]
        public void RegistryServer_ReturnsValuePassedToConstructor()
        {
            var mockClient = Substitute.For<ContainerRegistryContentClient>();
            var uploader = new ContainerRegistryTemplateUploader(mockClient, "myregistry.azurecr.io");

            Assert.Equal("myregistry.azurecr.io", uploader.RegistryServer);
        }
    }
}
