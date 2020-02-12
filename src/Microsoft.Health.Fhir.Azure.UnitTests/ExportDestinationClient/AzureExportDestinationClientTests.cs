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
        private IAccessTokenProvider _accessTokenProvider;
        private ILogger<AzureExportDestinationClient> _logger;

        private AzureExportDestinationClient _exportDestinationClient;

        public AzureExportDestinationClientTests()
        {
            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _accessTokenProvider = Substitute.For<IAccessTokenProvider>();
            _logger = Substitute.For<ILogger<AzureExportDestinationClient>>();

            _exportDestinationClient = new AzureExportDestinationClient(optionsExportConfig, _accessTokenProvider, _logger);
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

        [Fact]
        public async Task GivenUnableToGetAccessToken_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            string validUri = "https://localhost/storage";

            // Set up access token provider to throw exception when invoked
            IAccessTokenProvider mockAccessTokenProvider = Substitute.For<IAccessTokenProvider>();
            mockAccessTokenProvider.GetAccessTokenForResourceAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns<string>(x => throw new AccessTokenProviderException("cant get access token"));

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(validUri, CancellationToken.None));

            Assert.Contains(Resources.CannotGetAccessToken, exception.Message);
            Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        }
    }
}
