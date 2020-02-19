// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class AzureAccessTokenClientInitializerTests
    {
        private ExportJobConfiguration _exportJobConfiguration;
        private IAccessTokenProvider _accessTokenProvider;
        private ILogger<AzureAccessTokenClientInitializer> _logger;

        private AzureAccessTokenClientInitializer _azureAccessTokenClientInitializer;

        public AzureAccessTokenClientInitializerTests()
        {
            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _accessTokenProvider = Substitute.For<IAccessTokenProvider>();
            _logger = Substitute.For<ILogger<AzureAccessTokenClientInitializer>>();

            _azureAccessTokenClientInitializer = new AzureAccessTokenClientInitializer(_accessTokenProvider, optionsExportConfig, _logger);
        }

        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [Theory]
        public async Task GivenNullOrEmptyStorageUri_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string storageUriString)
        {
            _exportJobConfiguration.StorageAccountUri = storageUriString;

            var exception = await Assert.ThrowsAsync<ExportClientInitializerException>(() => _azureAccessTokenClientInitializer.GetAuthorizedClientAsync(CancellationToken.None));
            Assert.Contains(Resources.InvalidStorageUri, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [InlineData("randomUri")]
        [InlineData("https://")]
        [Theory]
        public async Task GivenInvalidStorageUri_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string storageUriString)
        {
            _exportJobConfiguration.StorageAccountUri = storageUriString;

            var exception = await Assert.ThrowsAsync<ExportClientInitializerException>(() => _azureAccessTokenClientInitializer.GetAuthorizedClientAsync(CancellationToken.None));
            Assert.Contains(Resources.InvalidStorageUri, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenUnableToGetAccessToken_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown()
        {
            _exportJobConfiguration.StorageAccountUri = "https://localhost/storage";

            // Set up access token provider to throw exception when invoked
            _accessTokenProvider.GetAccessTokenForResourceAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns<string>(x => throw new AccessTokenProviderException("cant get access token"));

            var exception = await Assert.ThrowsAsync<ExportClientInitializerException>(() => _azureAccessTokenClientInitializer.GetAuthorizedClientAsync(CancellationToken.None));

            Assert.Contains(Resources.CannotGetAccessToken, exception.Message);
            Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        }

        [Fact]
        public async Task GivenAbleToGetAccessToken_WhenGetAuthorizedClientAsync_ThenClientIsReturned()
        {
            _exportJobConfiguration.StorageAccountUri = "https://localhost/storage";

            // Set up access token provider to return access token when invoked.
            _accessTokenProvider.GetAccessTokenForResourceAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns<string>("randomAccessToken");

            CloudBlobClient client = await _azureAccessTokenClientInitializer.GetAuthorizedClientAsync(CancellationToken.None);

            Assert.NotNull(client);
        }
    }
}
