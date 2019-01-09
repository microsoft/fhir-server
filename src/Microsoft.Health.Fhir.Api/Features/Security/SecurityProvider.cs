// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    public class SecurityProvider : IProvideCapability
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly ILogger<SecurityConfiguration> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IUrlResolver _urlResolver;

        public SecurityProvider(IOptions<SecurityConfiguration> securityConfiguration, IHttpClientFactory httpClientFactory, ILogger<SecurityConfiguration> logger, IUrlResolver urlResolver)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _urlResolver = urlResolver;
        }

        public void Build(ListedCapabilityStatement statement)
        {
            if (_securityConfiguration.Enabled)
            {
                if (_securityConfiguration.EnableAadSmartOnFhirProxy)
                {
                    statement.AddProxyOAuthSecurityService(_urlResolver.ResolveMetadataUrl(false));
                }
                else
                {
                    statement.AddOAuthSecurityService(_securityConfiguration.Authentication.Authority, _httpClientFactory, _logger);
                }
            }
        }
    }
}
