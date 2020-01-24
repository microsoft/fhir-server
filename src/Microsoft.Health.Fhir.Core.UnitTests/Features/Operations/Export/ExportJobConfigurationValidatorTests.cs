// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportJobConfigurationValidatorTests
    {
        private ExportJobConfiguration _exportJobConfiguration;
        private IExportDestinationClientFactory _exportDestinationClientFactory;
        private IAccessTokenProviderFactory _accessTokenProviderFactory;

        private ExportJobConfigurationValidator _exportJobConfigurationValidator;

        public ExportJobConfigurationValidatorTests()
        {
            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsConfig.Value.Returns(_exportJobConfiguration);

            _exportDestinationClientFactory = Substitute.For<IExportDestinationClientFactory>();
            _accessTokenProviderFactory = Substitute.For<IAccessTokenProviderFactory>();

            _exportJobConfigurationValidator = new ExportJobConfigurationValidator(
                optionsConfig,
                _exportDestinationClientFactory,
                _accessTokenProviderFactory,
                Substitute.For<ILogger<ExportJobConfigurationValidator>>());
        }

        [Fact]
        public void GivenUnsupportedDestinationType_WhenValidateExportJobConfig_ThenThrowsException()
        {
            string destinationType = "unsupportedDestination";
            _exportJobConfiguration.StorageAccountType = destinationType;

            _exportDestinationClientFactory.IsSupportedDestinationType(Arg.Is(destinationType)).Returns(false);

            var ex = Assert.Throws<ExportJobConfigValidationException>(() => _exportJobConfigurationValidator.ValidateExportJobConfig());
            Assert.Contains("destination type", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public void GivenInvalidDestinationType_WhenValidateExportJobConfig_ThenThrowsException(string destinationType)
        {
            _exportJobConfiguration.StorageAccountType = destinationType;

            var ex = Assert.Throws<ExportJobConfigValidationException>(() => _exportJobConfigurationValidator.ValidateExportJobConfig());
            Assert.Contains("destination type", ex.Message);
        }

        [Fact]
        public void GivenDestinationConnectionIsUriAndAccessTokenProviderNotSupported_WhenValidateExportJobConfig_ThenThrowsException()
        {
            _exportJobConfiguration.StorageAccountType = "supportedDestination";
            string destinationConnection = "https://localhost/uri";
            _exportJobConfiguration.StorageAccountConnection = destinationConnection;
            string accessTokenProviderType = "accessTokenProvider";
            _exportJobConfiguration.AccessTokenProviderType = accessTokenProviderType;

            _exportDestinationClientFactory.IsSupportedDestinationType(Arg.Any<string>()).Returns(true);
            _accessTokenProviderFactory.IsSupportedAccessTokenProviderType(Arg.Is(accessTokenProviderType)).Returns(false);

            var ex = Assert.Throws<ExportJobConfigValidationException>(() => _exportJobConfigurationValidator.ValidateExportJobConfig());
            Assert.Contains("access token provider", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public void GivenDestinationConnectionIsUriAndInvalidAccessTokenProviderType_WhenValidateExportJobConfig_ThenThrowsException(string accessTokenProviderType)
        {
            string destinationConnection = "https://localhost/uri";
            _exportJobConfiguration.StorageAccountConnection = destinationConnection;
            _exportJobConfiguration.AccessTokenProviderType = accessTokenProviderType;

            _exportJobConfiguration.StorageAccountType = "supportedDestination";
            _exportDestinationClientFactory.IsSupportedDestinationType(Arg.Any<string>()).Returns(true);

            var ex = Assert.Throws<ExportJobConfigValidationException>(() => _exportJobConfigurationValidator.ValidateExportJobConfig());
            Assert.Contains("access token provider", ex.Message);
        }

        [Fact]
        public void GivenDestinationConnectionIsUriAndAccessTokenProviderIsSupported_WhenValidateExportJobConfig_ThenCompletesSuccessfully()
        {
            string destinationConnection = "https://localhost/uri";
            _exportJobConfiguration.StorageAccountConnection = destinationConnection;
            string accessTokenProviderType = "accessTokenProvider";
            _exportJobConfiguration.AccessTokenProviderType = accessTokenProviderType;

            _exportJobConfiguration.StorageAccountType = "supportedDestination";
            _exportDestinationClientFactory.IsSupportedDestinationType(Arg.Any<string>()).Returns(true);
            _accessTokenProviderFactory.IsSupportedAccessTokenProviderType(Arg.Is(accessTokenProviderType)).Returns(true);

            Assert.True(_exportJobConfigurationValidator.ValidateExportJobConfig());
        }

        [Fact]
        public void GivenDestinationConnectionIsNotAnUri_WhenValidateExportJobConfig_ThenWeDontCallAccessTokenProviderFactory()
        {
            string destinationConnection = "nonUri";
            _exportJobConfiguration.StorageAccountConnection = destinationConnection;

            _exportJobConfiguration.StorageAccountType = "supportedDestination";
            _exportDestinationClientFactory.IsSupportedDestinationType(Arg.Any<string>()).Returns(true);

            _accessTokenProviderFactory.DidNotReceive().IsSupportedAccessTokenProviderType(Arg.Any<string>());
        }

        [Fact]
        public void GivenDestinationConnectionIsNonBase64EncodedString_WhenValidateExportJobConfig_ThenThrowsException()
        {
            string destinationConnection = "***nonbase64EncodedString***";
            _exportJobConfiguration.StorageAccountConnection = destinationConnection;

            _exportJobConfiguration.StorageAccountType = "supportedDestination";
            _exportDestinationClientFactory.IsSupportedDestinationType(Arg.Any<string>()).Returns(true);

            var ex = Assert.Throws<ExportJobConfigValidationException>(() => _exportJobConfigurationValidator.ValidateExportJobConfig());
            Assert.Contains("connection string", ex.Message);
        }

        [Fact]
        public void GivenDestinationConnectionIsBase64EncodedString_WhenValidateExportJobConfig_ThenCompletesSuccessfully()
        {
            string destinationConnection = Convert.ToBase64String(Encoding.ASCII.GetBytes("base64encodedstring"));
            _exportJobConfiguration.StorageAccountConnection = destinationConnection;

            _exportJobConfiguration.StorageAccountType = "supportedDestination";
            _exportDestinationClientFactory.IsSupportedDestinationType(Arg.Any<string>()).Returns(true);

            Assert.True(_exportJobConfigurationValidator.ValidateExportJobConfig());
        }
    }
}
