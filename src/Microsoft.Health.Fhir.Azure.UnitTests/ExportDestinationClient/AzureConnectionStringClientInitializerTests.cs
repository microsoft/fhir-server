// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    public class AzureConnectionStringClientInitializerTests
    {
        private ExportJobConfiguration _exportJobConfiguration;
        private ILogger<AzureConnectionStringClientInitializer> _logger;

        private AzureConnectionStringClientInitializer _azureConnectionStringClientInitializer;

        public AzureConnectionStringClientInitializerTests()
        {
            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _logger = Substitute.For<ILogger<AzureConnectionStringClientInitializer>>();

            _azureConnectionStringClientInitializer = new AzureConnectionStringClientInitializer(optionsExportConfig, _logger);
        }

        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("storageAccount")]
        [InlineData("connectionString")]
        [Theory]
        public async Task GivenInvalidStorageAccountConnectionString_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string connectionString)
        {
            _exportJobConfiguration.StorageAccountConnection = connectionString;

            var exception = await Assert.ThrowsAsync<ExportClientInitializerException>(() => _azureConnectionStringClientInitializer.GetAuthorizedClientAsync(CancellationToken.None));
            Assert.Contains(Resources.InvalidConnectionSettings, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public async Task GivenValidStorageAccountConnectionString_WhenGetAuthorizedClientAsync_ThenClientIsReturned()
        {
            _exportJobConfiguration.StorageAccountConnection = "DefaultEndpointsProtocol=https;AccountName=randomName;AccountKey=randomString;EndpointSuffix=core.windows.net";

            CloudBlobClient client = await _azureConnectionStringClientInitializer.GetAuthorizedClientAsync(CancellationToken.None);

            Assert.NotNull(client);
        }
    }
}
