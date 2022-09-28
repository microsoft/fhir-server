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
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
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
        public void GivenInvalidStorageAccountConnectionString_WhenGetAuthorizedClientAsync_ThenExportClientInitializerExceptionIsThrown(string connectionString)
        {
            _exportJobConfiguration.StorageAccountConnection = connectionString;

            var exception = Assert.Throws<ExportClientInitializerException>(() => _azureConnectionStringClientInitializer.GetAuthorizedClient());
            Assert.Contains(Resources.InvalidConnectionSettings, exception.Message);
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Fact]
        public void GivenValidStorageAccountConnectionString_WhenGetAuthorizedClientAsync_ThenClientIsReturned()
        {
            _exportJobConfiguration.StorageAccountConnection = "DefaultEndpointsProtocol=https;AccountName=randomName;AccountKey=randomString;EndpointSuffix=core.windows.net";

            BlobServiceClient client = _azureConnectionStringClientInitializer.GetAuthorizedClient();

            Assert.NotNull(client);
        }
    }
}
