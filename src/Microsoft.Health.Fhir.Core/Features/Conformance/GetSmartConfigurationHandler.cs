// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetSmartConfigurationHandler : IRequestHandler<GetSmartConfigurationRequest, GetSmartConfigurationResponse>
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly SmartIdentityProviderConfiguration _smartIdentityProviderConfiguration;
        private readonly IOidcDiscoveryService _oidcDiscoveryService;

        public GetSmartConfigurationHandler(
            IOptions<SecurityConfiguration> securityConfigurationOptions,
            IOptions<SmartIdentityProviderConfiguration> smartIdentityProviderConfiguration,
            IOidcDiscoveryService oidcDiscoveryService)
        {
            EnsureArg.IsNotNull(securityConfigurationOptions?.Value, nameof(securityConfigurationOptions));
            EnsureArg.IsNotNull(smartIdentityProviderConfiguration?.Value, nameof(smartIdentityProviderConfiguration));
            EnsureArg.IsNotNull(oidcDiscoveryService, nameof(oidcDiscoveryService));

            _securityConfiguration = securityConfigurationOptions.Value;
            _smartIdentityProviderConfiguration = smartIdentityProviderConfiguration.Value;
            _oidcDiscoveryService = oidcDiscoveryService;
        }

        public async Task<GetSmartConfigurationResponse> Handle(GetSmartConfigurationRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (_securityConfiguration.Authorization.Enabled || _securityConfiguration.Authorization.EnableSmartWithoutAuth)
            {
                try
                {
                    string baseEndpoint = GetAuthority();

                    Uri authorizationEndpoint;
                    Uri tokenEndpoint;
                    string issuer;
                    string jwksUri;

                    if (_securityConfiguration.EnableAadSmartOnFhirProxy)
                    {
                        authorizationEndpoint = new Uri(request.BaseUri, "AadSmartOnFhirProxy/authorize");
                        tokenEndpoint = new Uri(request.BaseUri, "AadSmartOnFhirProxy/token");

                        // Still resolve issuer and jwks_uri from OIDC discovery
                        (_, _, issuer, jwksUri) = await _oidcDiscoveryService.ResolveEndpointsAsync(baseEndpoint, cancellationToken);
                    }
                    else
                    {
                        (authorizationEndpoint, tokenEndpoint, issuer, jwksUri) = await _oidcDiscoveryService.ResolveEndpointsAsync(baseEndpoint, cancellationToken);
                    }

                    ICollection<string> capabilities = new List<string>(
                        Constants.SmartCapabilityClients
                            .Concat(Constants.SmartCapabilityAdditional)
                            .Concat(Constants.SmartCapabilityLaunches)
                            .Concat(Constants.SmartCapabilityPermissions)
                            .Concat(Constants.SmartCapabilitySSOs));

                    if (!string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Authority))
                    {
                        ((List<string>)capabilities).AddRange(Constants.SmartCapabilityThirdPartyContexts);
                    }

                    // Add SMART v2 scope support - these are the core scopes supported natively by the FHIR service
                    ICollection<string> scopesSupported = new List<string>
                    {
                        // Standard OAuth/OIDC scopes
                        "openid",
                        "fhirUser",
                        "launch",
                        "launch/patient",
                        "offline_access",
                        "online_access",
                    };

                    ICollection<string> codeChallengeMethodsSupported = new List<string>
                    {
                        "S256",
                    };

                    ICollection<string> grantTypesSupported = new List<string>
                    {
                        "authorization_code",
                        "client_credentials",
                    };

                    ICollection<string> tokenEndpointAuthMethodsSupported = new List<string>
                    {
                        "client_secret_basic",
                        "client_secret_jwt",
                        "none",
                    };

                    ICollection<string> responseTypesSupported = new List<string>
                    {
                        "code",
                    };

                    string introspectionEndpoint = !string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Introspection)
                        ? _smartIdentityProviderConfiguration.Introspection
                        : new Uri(request.BaseUri, "connect/introspect").ToString();

                    return new GetSmartConfigurationResponse(
                        authorizationEndpoint,
                        tokenEndpoint,
                        capabilities,
                        scopesSupported,
                        codeChallengeMethodsSupported,
                        grantTypesSupported,
                        tokenEndpointAuthMethodsSupported,
                        responseTypesSupported,
                        introspectionEndpoint,
                        _smartIdentityProviderConfiguration.Management,
                        _smartIdentityProviderConfiguration.Revocation,
                        issuer,
                        jwksUri);
                }
                catch (Exception e) when (e is ArgumentNullException || e is UriFormatException)
                {
                    throw new OperationFailedException(
                        string.Format(Core.Resources.InvalidSecurityConfigurationBaseEndpoint, nameof(SecurityConfiguration.Authentication.Authority)),
                        HttpStatusCode.BadRequest);
                }
            }

            throw new OperationFailedException(
                Core.Resources.SecurityConfigurationAuthorizationNotEnabled,
                HttpStatusCode.BadRequest);
        }

        private string GetAuthority()
        {
            var authority = !string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Authority) ?
                _smartIdentityProviderConfiguration.Authority : _securityConfiguration.Authentication.Authority;
            return authority?.TrimEnd('/');
        }
    }
}
