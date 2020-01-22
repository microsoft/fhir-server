// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportJobConfigurationValidator : IExportJobConfigurationValidator
    {
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly IExportDestinationClientFactory _exportDestinationClientFactory;
        private readonly IAccessTokenProviderFactory _accessTokenProviderFactory;
        private readonly ILogger<ExportJobConfigurationValidator> _logger;

        public ExportJobConfigurationValidator(
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            IExportDestinationClientFactory exportDestinationClientFactory,
            IAccessTokenProviderFactory accessTokenProviderFactory,
            ILogger<ExportJobConfigurationValidator> logger)
        {
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(exportDestinationClientFactory, nameof(exportDestinationClientFactory));
            EnsureArg.IsNotNull(accessTokenProviderFactory, nameof(accessTokenProviderFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportJobConfiguration = exportJobConfiguration.Value;
            _exportDestinationClientFactory = exportDestinationClientFactory;
            _accessTokenProviderFactory = accessTokenProviderFactory;
            _logger = logger;
        }

        public bool ValidateExportJobConfig()
        {
            if (string.IsNullOrWhiteSpace(_exportJobConfiguration.DefaultStorageAccountType) ||
                !_exportDestinationClientFactory.IsSupportedDestinationType(_exportJobConfiguration.DefaultStorageAccountType))
            {
                throw new ExportJobConfigValidationException(string.Format(Resources.UnsupportedDestinationTypeMessage, _exportJobConfiguration.DefaultStorageAccountType), HttpStatusCode.BadRequest);
            }

            // Check whether the config contains a uri to a storage account or a connection string.
            if (Uri.TryCreate(_exportJobConfiguration.DefaultStorageAccountConnection, UriKind.Absolute, out Uri resultUri))
            {
                // We need to validate whether we support the corresponding access token provider.
                if (string.IsNullOrWhiteSpace(_exportJobConfiguration.AccessTokenProviderType) ||
                    !_accessTokenProviderFactory.IsSupportedAccessTokenProviderType(_exportJobConfiguration.AccessTokenProviderType))
                {
                    throw new ExportJobConfigValidationException(string.Format(Resources.UnsupportedAccessTokenProvider, _exportJobConfiguration.AccessTokenProviderType), HttpStatusCode.BadRequest);
                }
            }
            else
            {
                try
                {
                    Encoding.UTF8.GetString(Convert.FromBase64String(_exportJobConfiguration.DefaultStorageAccountConnection));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to parse connection string");
                    throw new ExportJobConfigValidationException(Resources.InvalidConnectionString, HttpStatusCode.BadRequest);
                }
            }

            return true;
        }
    }
}
