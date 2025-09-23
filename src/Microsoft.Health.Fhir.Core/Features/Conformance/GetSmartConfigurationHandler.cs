// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Providers;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetSmartConfigurationHandler : IRequestHandler<GetSmartConfigurationRequest, GetSmartConfigurationResponse>
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IWellKnownConfigurationProvider _configurationProvider;
        private readonly ILogger<GetSmartConfigurationHandler> _logger;

        public GetSmartConfigurationHandler(
            IOptions<SecurityConfiguration> securityConfigurationOptions,
            IWellKnownConfigurationProvider configurationProvider,
            ILogger<GetSmartConfigurationHandler> logger)
        {
            _securityConfiguration = EnsureArg.IsNotNull(securityConfigurationOptions?.Value, nameof(securityConfigurationOptions));
            _configurationProvider = EnsureArg.IsNotNull(configurationProvider, nameof(configurationProvider));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<GetSmartConfigurationResponse> Handle(GetSmartConfigurationRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            _logger.LogInformation("Starting processing of request to .well-known/smart-configuration endpoint.");

            if (!_securityConfiguration.Authorization.Enabled && !_securityConfiguration.Authorization.EnableSmartWithoutAuth)
            {
                _logger.LogInformation("Security configuration is not enabled cannot process .well-known/smart-configuration request.");

                throw new OperationFailedException(
                    Core.Resources.SecurityConfigurationAuthorizationNotEnabled,
                    HttpStatusCode.BadRequest);
            }

            GetSmartConfigurationResponse smartConfiguration = await _configurationProvider.GetSmartConfigurationAsync(cancellationToken);

            if (smartConfiguration == null)
            {
                _logger.LogInformation("Identity provider does not support .well-known/smart-configuration using .well-known/openid-configuration instead.");

                // If the SMART configuration failed, fall back to the OpenID configuration.
                OpenIdConfigurationResponse openIdResponse = await _configurationProvider.GetOpenIdConfigurationAsync(cancellationToken);

                if (openIdResponse?.AuthorizationEndpoint != null && openIdResponse?.TokenEndpoint != null)
                {
                    smartConfiguration = new GetSmartConfigurationResponse(openIdResponse.AuthorizationEndpoint, openIdResponse.TokenEndpoint);
                }
            }

            if (smartConfiguration != null)
            {
                if (smartConfiguration.Capabilities.Count < 1)
                {
                    // Ensure the SMART configuration capabilities are populated with the minimum FHIR server capabilities.
                    smartConfiguration.Capabilities.Add("sso-openid-connect");
                    smartConfiguration.Capabilities.Add("permission-offline");
                    smartConfiguration.Capabilities.Add("permission-patient");
                    smartConfiguration.Capabilities.Add("permission-user");
                }

                return smartConfiguration;
            }

            throw new OperationFailedException(
                string.Format(Core.Resources.InvalidSecurityConfigurationBaseEndpoint, nameof(SecurityConfiguration.Authentication.Authority)),
                HttpStatusCode.BadRequest);
        }
    }
}
