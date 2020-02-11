// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class AzureExportDestinationClientTests
    {
        private ExportJobConfiguration _exportJobConfiguration;
        private IAccessTokenProviderFactory _accessTokenProviderFactory;
        private ILogger<AzureExportDestinationClient> _logger;

        private AzureExportDestinationClient _exportDestinationClient;

        public AzureExportDestinationClientTests()
        {
            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _accessTokenProviderFactory = Substitute.For<IAccessTokenProviderFactory>();
            _logger = Substitute.For<ILogger<AzureExportDestinationClient>>();

            _exportDestinationClient = new AzureExportDestinationClient(optionsExportConfig, _accessTokenProviderFactory, _logger);
        }

        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [Theory]
        public async Task GivenNullOrEmptyConnectString_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown(string connectionString)
        {
            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(connectionString, CancellationToken.None));
            Assert.Contains(Resources.InvalidConnectionSettings, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenInvalidCloudAccountConnectString_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            string encodedInvalidString = Convert.ToBase64String(Encoding.UTF8.GetBytes("invalidStorageAccount"));

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(encodedInvalidString, CancellationToken.None));
            Assert.Contains(Resources.InvalidConnectionSettings, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenInvalidStorageUri_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            string invalidUri = "https://";

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(invalidUri, CancellationToken.None));
            Assert.Contains(Resources.InvalidStorageUri, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [InlineData("")]
        [InlineData(null)]
        [InlineData("  ")]
        [Theory]
        public async Task GivenValidStorageUriInvalidAccessTokenProviderType_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown(string accessTokenProviderType)
        {
            string validUri = "https://localhost/storage";
            _exportJobConfiguration.AccessTokenProviderType = accessTokenProviderType;

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(validUri, CancellationToken.None));

            Assert.Contains(Resources.UnsupportedAccessTokenProviderType, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenValidStorageUriUnsupportedAccessTokenProviderType_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            string validUri = "https://localhost/storage";
            _exportJobConfiguration.AccessTokenProviderType = "unsupportedAccessTokenProviderType";

            _accessTokenProviderFactory.IsSupportedAccessTokenProviderType(Arg.Is(_exportJobConfiguration.AccessTokenProviderType)).Returns(false);

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(validUri, CancellationToken.None));

            Assert.Contains(Resources.UnsupportedAccessTokenProviderType, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenUnableToGetAccessToken_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            string validUri = "https://localhost/storage";
            _exportJobConfiguration.AccessTokenProviderType = "supportedAccessTokenProviderType";

            // Set up access token provider to throw exception when invoked
            IAccessTokenProvider mockAccessTokenProvider = Substitute.For<IAccessTokenProvider>();
            mockAccessTokenProvider.GetAccessTokenForResourceAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns<string>(x => throw new AccessTokenProviderException("cant get access token"));
            _accessTokenProviderFactory.IsSupportedAccessTokenProviderType(Arg.Is(_exportJobConfiguration.AccessTokenProviderType)).Returns(true);

            _accessTokenProviderFactory.Create(Arg.Is(_exportJobConfiguration.AccessTokenProviderType)).Returns(mockAccessTokenProvider);

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(validUri, CancellationToken.None));

            Assert.Contains(Resources.CannotGetAccessToken, exception.Message);
            Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        }
    }
}
