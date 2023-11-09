// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class AzureAccessTokenClientInitializerTests
    {
        private ExportJobConfiguration _exportJobConfiguration;
        private ILogger<AzureAccessTokenClientInitializer> _logger;

        private AzureAccessTokenClientInitializer _azureAccessTokenClientInitializer;

        public AzureAccessTokenClientInitializerTests()
        {
            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Options.Create(_exportJobConfiguration);

            _logger = Substitute.For<ILogger<AzureAccessTokenClientInitializer>>();

            _azureAccessTokenClientInitializer = new AzureAccessTokenClientInitializer(optionsExportConfig, _logger);
        }

        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [Theory]
        public void GivenNullOrEmptyStorageUri_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string storageUriString)
        {
            _exportJobConfiguration.StorageAccountUri = storageUriString;

            var exception = Assert.Throws<ExportClientInitializerException>(() => _azureAccessTokenClientInitializer.GetAuthorizedClient());
            Assert.Contains(Resources.InvalidStorageUri, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [InlineData("randomUri")]
        [InlineData("https://")]
        [Theory]
        public void GivenInvalidStorageUri_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string storageUriString)
        {
            _exportJobConfiguration.StorageAccountUri = storageUriString;

            var exception = Assert.Throws<ExportClientInitializerException>(() => _azureAccessTokenClientInitializer.GetAuthorizedClient());
            Assert.Contains(Resources.InvalidStorageUri, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public void GivenAbleToGetAccessToken_WhenGetAuthorizedClientAsync_ThenClientIsReturned()
        {
            _exportJobConfiguration.StorageAccountUri = "https://localhost/storage";

            BlobServiceClient client = _azureAccessTokenClientInitializer.GetAuthorizedClient();

            Assert.NotNull(client);
        }
    }
}
