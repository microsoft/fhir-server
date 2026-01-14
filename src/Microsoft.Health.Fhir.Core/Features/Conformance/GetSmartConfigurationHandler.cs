// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetSmartConfigurationHandler : IRequestHandler<GetSmartConfigurationRequest, GetSmartConfigurationResponse>
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly SmartIdentityProviderConfiguration _smartIdentityProviderConfiguration;

        public GetSmartConfigurationHandler(
            IOptions<SecurityConfiguration> securityConfigurationOptions,
            IOptions<SmartIdentityProviderConfiguration> smartIdentityProviderConfiguration)
        {
            EnsureArg.IsNotNull(securityConfigurationOptions?.Value, nameof(securityConfigurationOptions));
            EnsureArg.IsNotNull(smartIdentityProviderConfiguration?.Value, nameof(smartIdentityProviderConfiguration));

            _securityConfiguration = securityConfigurationOptions.Value;
            _smartIdentityProviderConfiguration = smartIdentityProviderConfiguration.Value;
        }

        public Task<GetSmartConfigurationResponse> HandleAsync(GetSmartConfigurationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handle(request));
        }

        protected GetSmartConfigurationResponse Handle(GetSmartConfigurationRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (_securityConfiguration.Authorization.Enabled || _securityConfiguration.Authorization.EnableSmartWithoutAuth)
            {
                try
                {
                    string baseEndpoint = GetAuthority();
                    Uri authorizationEndpoint = new Uri(baseEndpoint + "/authorize");
                    Uri tokenEndpoint = new Uri(baseEndpoint + "/token");

                    if (_securityConfiguration.EnableAadSmartOnFhirProxy)
                    {
                        authorizationEndpoint = new Uri(request.BaseUri, "AadSmartOnFhirProxy/authorize");
                        tokenEndpoint = new Uri(request.BaseUri, "AadSmartOnFhirProxy/token");
                    }

                    ICollection<string> capabilities = new List<string>
                    {
                        "sso-openid-connect",
                        "permission-offline",
                        "permission-patient",
                        "permission-user",
                    };

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

                    return new GetSmartConfigurationResponse(
                        authorizationEndpoint,
                        tokenEndpoint,
                        capabilities,
                        scopesSupported,
                        codeChallengeMethodsSupported,
                        grantTypesSupported,
                        tokenEndpointAuthMethodsSupported,
                        responseTypesSupported,
                        _smartIdentityProviderConfiguration.Introspection,
                        _smartIdentityProviderConfiguration.Management,
                        _smartIdentityProviderConfiguration.Revocation);
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
