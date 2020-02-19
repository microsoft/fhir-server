// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class AzureExportDestinationClientTests
    {
        private IExportClientInitializer<CloudBlobClient> _exportClientInitializer;
        private ILogger<AzureExportDestinationClient> _logger;

        private AzureExportDestinationClient _exportDestinationClient;

        public AzureExportDestinationClientTests()
        {
            _exportClientInitializer = Substitute.For<IExportClientInitializer<CloudBlobClient>>();
            _logger = Substitute.For<ILogger<AzureExportDestinationClient>>();

            _exportDestinationClient = new AzureExportDestinationClient(_exportClientInitializer, _logger);
        }

        [Fact]
        public async Task GivenUnableToInitializeExportClient_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            string message = "Can't initialize client";
            HttpStatusCode statusCode = HttpStatusCode.BadRequest;

            _exportClientInitializer.GetAuthorizedClientAsync(Arg.Any<CancellationToken>()).Returns<CloudBlobClient>(x => throw new ExportClientInitializerException(message, statusCode));

            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(CancellationToken.None));

            Assert.Contains(message, exception.Message);
            Assert.Equal(statusCode, exception.StatusCode);
        }
    }
}
