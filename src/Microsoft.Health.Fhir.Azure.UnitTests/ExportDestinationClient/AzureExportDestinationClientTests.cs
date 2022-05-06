// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class AzureExportDestinationClientTests
    {
        private IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private ExportJobConfiguration _exportJobConfiguration;
        private ILogger<AzureExportDestinationClient> _logger;

        private AzureExportDestinationClient _exportDestinationClient;

        public AzureExportDestinationClientTests()
        {
            _exportClientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            _logger = Substitute.For<ILogger<AzureExportDestinationClient>>();

            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _exportDestinationClient = new AzureExportDestinationClient(_exportClientInitializer, optionsExportConfig, _logger);
        }

        [Fact]
        public async Task GivenUnableToInitializeExportClient_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            string message = "Can't initialize client";
            HttpStatusCode statusCode = HttpStatusCode.BadRequest;

            _exportClientInitializer.GetAuthorizedClient(Arg.Any<ExportJobConfiguration>()).Returns<BlobServiceClient>(x => throw new ExportClientInitializerException(message, statusCode));

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(CancellationToken.None));

            Assert.Contains(message, exception.Message);
            Assert.Equal(statusCode, exception.StatusCode);
        }
    }
}
