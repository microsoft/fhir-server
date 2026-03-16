// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    public class SecurityProvider : IProvideCapability
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly ILogger<SecurityConfiguration> _logger;
        private readonly IOidcDiscoveryService _oidcDiscoveryService;
        private readonly IUrlResolver _urlResolver;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly SmartIdentityProviderConfiguration _smartIdentityProviderConfiguration;

        public SecurityProvider(
            IOptions<SecurityConfiguration> securityConfiguration,
            IOidcDiscoveryService oidcDiscoveryService,
            ILogger<SecurityConfiguration> logger,
            IUrlResolver urlResolver,
            IModelInfoProvider modelInfoProvider,
            IOptions<SmartIdentityProviderConfiguration> smartIdentityProviderConfiguration)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(securityConfiguration.Value.Authentication.Authority, nameof(securityConfiguration.Value.Authentication.Authority));
            EnsureArg.IsNotNull(oidcDiscoveryService, nameof(oidcDiscoveryService));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(smartIdentityProviderConfiguration?.Value, nameof(smartIdentityProviderConfiguration));

            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
            _oidcDiscoveryService = oidcDiscoveryService;
            _urlResolver = urlResolver;
            _modelInfoProvider = modelInfoProvider;
            _smartIdentityProviderConfiguration = smartIdentityProviderConfiguration.Value;
        }

        public async Task BuildAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken)
        {
            if (_securityConfiguration.Enabled)
            {
                try
                {
                    if (_securityConfiguration.EnableAadSmartOnFhirProxy)
                    {
                        builder.Apply(statement =>
                        {
                            AddProxyOAuthSecurityService(statement, RouteNames.AadSmartOnFhirProxyAuthorize, RouteNames.AadSmartOnFhirProxyToken);
                        });
                    }
                    else
                    {
                        var (authorizationEndpoint, tokenEndpoint) = await _oidcDiscoveryService.ResolveEndpointsAsync(
                            _securityConfiguration.Authentication.Authority,
                            cancellationToken);

                        builder.Apply(statement =>
                        {
                            AddOAuthSecurityService(statement, authorizationEndpoint.AbsoluteUri, tokenEndpoint.AbsoluteUri);
                        });
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "SecurityProvider failed creating a new Capability Statement.");
                    throw;
                }
            }
        }

        private void AddProxyOAuthSecurityService(ListedCapabilityStatement statement, string authorizeRouteName, string tokenRouteName)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNullOrWhiteSpace(authorizeRouteName, nameof(authorizeRouteName));
            EnsureArg.IsNotNullOrWhiteSpace(tokenRouteName, nameof(tokenRouteName));

            ListedRestComponent restComponent = statement.Rest.Server();
            SecurityComponent security = restComponent.Security ?? new SecurityComponent();

            var codableConceptInfo = new CodableConceptInfo();
            security.Service.Add(codableConceptInfo);

            codableConceptInfo.Coding.Add(_modelInfoProvider.Version == FhirSpecification.Stu3
                ? Constants.RestfulSecurityServiceStu3OAuth
                : Constants.RestfulSecurityServiceOAuth);

            Uri tokenEndpoint = _urlResolver.ResolveRouteNameUrl(tokenRouteName, null);
            Uri authorizationEndpoint = _urlResolver.ResolveRouteNameUrl(authorizeRouteName, null);

            var smartExtensions = CreateSmartExtensions(authorizationEndpoint.AbsoluteUri, tokenEndpoint.AbsoluteUri);
            foreach (var extension in smartExtensions)
            {
                security.Extension.Add(extension);
            }

            restComponent.Security = security;
        }

        private void AddOAuthSecurityService(ListedCapabilityStatement statement, string authorizationEndpoint, string tokenEndpoint)
        {
            ListedRestComponent restComponent = statement.Rest.Server();
            SecurityComponent security = restComponent.Security ?? new SecurityComponent();

            var codableConceptInfo = new CodableConceptInfo();
            security.Service.Add(codableConceptInfo);

            codableConceptInfo.Coding.Add(_modelInfoProvider.Version == FhirSpecification.Stu3
                ? Constants.RestfulSecurityServiceStu3OAuth
                : Constants.RestfulSecurityServiceOAuth);

            var smartExtensions = CreateSmartExtensions(authorizationEndpoint, tokenEndpoint);
            foreach (var extension in smartExtensions)
            {
                security.Extension.Add(extension);
            }

            restComponent.Security = security;
        }

        private List<JObject> CreateSmartExtensions(string authorizationEndpoint, string tokenEndpoint)
        {
            var extensions = new List<JObject>();
            extensions.Add(GetUrlExtension(authorizationEndpoint, tokenEndpoint));
            extensions.AddRange(GetAdditionalCapabilities());
            extensions.AddRange(GetClientCapabilities());
            extensions.AddRange(GetContextCapabilities());
            extensions.AddRange(GetLaunchCapabilities());
            extensions.AddRange(GetPermissionCapabilities());
            extensions.AddRange(GetSSOCapabilities());
            return extensions;
        }

        private string GetAuthorizationEndpoint(string authorizationEndpoint)
        {
            if (string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Authority))
            {
                return authorizationEndpoint;
            }

            return string.Format(
                "{0}/{1}",
                _smartIdentityProviderConfiguration.Authority.TrimEnd('/'),
                Constants.SmartOAuthUriExtensionAuthorize);
        }

        private string GetTokenEndpoint(string tokenEndpoint)
        {
            if (string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Authority))
            {
                return tokenEndpoint;
            }

            return string.Format(
                "{0}/{1}",
                _smartIdentityProviderConfiguration.Authority.TrimEnd('/'),
                Constants.SmartOAuthUriExtensionToken);
        }

        private JObject GetUrlExtension(string authorizationEndpoint, string tokenEndpoint)
        {
            var urls = new JArray(
                new JObject[]
                {
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartOAuthUriExtensionToken },
                        { Constants.ValueUriPropertyName, GetTokenEndpoint(tokenEndpoint) },
                    },
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartOAuthUriExtensionAuthorize },
                        { Constants.ValueUriPropertyName, GetAuthorizationEndpoint(authorizationEndpoint) },
                    },
                });

            if (!string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Introspection))
            {
                urls.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartOAuthUriExtensionIntrospection },
                        { Constants.ValueUriPropertyName, _smartIdentityProviderConfiguration.Introspection },
                    });
            }

            if (!string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Management))
            {
                urls.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartOAuthUriExtensionManagement },
                        { Constants.ValueUriPropertyName, _smartIdentityProviderConfiguration.Management },
                    });
            }

            if (!string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Revocation))
            {
                urls.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartOAuthUriExtensionRevocation },
                        { Constants.ValueUriPropertyName, _smartIdentityProviderConfiguration.Revocation },
                    });
            }

            return new JObject
            {
                { Constants.UrlPropertyName, Constants.SmartOAuthUriExtension },
                { Constants.ExtensionPropertyName, urls },
            };
        }

        private List<JObject> GetContextCapabilities()
        {
            var capabilities = new List<JObject>();
            if (IsThirdPartyIdentityProvider())
            {
                foreach (var context in Constants.SmartCapabilityThirdPartyContexts)
                {
                    capabilities.Add(
                        new JObject
                        {
                            { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                            { Constants.ValueCodePropertyName, context },
                        });
                }
            }

            return capabilities;
        }

        private static List<JObject> GetAdditionalCapabilities()
        {
            var capabilities = new List<JObject>();
            foreach (var client in Constants.SmartCapabilityAdditional)
            {
                capabilities.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                        { Constants.ValueCodePropertyName, client },
                    });
            }

            return capabilities;
        }

        private static List<JObject> GetClientCapabilities()
        {
            var capabilities = new List<JObject>();
            foreach (var client in Constants.SmartCapabilityClients)
            {
                capabilities.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                        { Constants.ValueCodePropertyName, client },
                    });
            }

            return capabilities;
        }

        private static List<JObject> GetLaunchCapabilities()
        {
            var capabilities = new List<JObject>();
            foreach (var launch in Constants.SmartCapabilityLaunches)
            {
                capabilities.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                        { Constants.ValueCodePropertyName, launch },
                    });
            }

            return capabilities;
        }

        private static List<JObject> GetPermissionCapabilities()
        {
            var capabilities = new List<JObject>();
            foreach (var permission in Constants.SmartCapabilityPermissions)
            {
                capabilities.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                        { Constants.ValueCodePropertyName, permission },
                    });
            }

            return capabilities;
        }

        private static List<JObject> GetSSOCapabilities()
        {
            var capabilities = new List<JObject>();
            foreach (var sso in Constants.SmartCapabilitySSOs)
            {
                capabilities.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                        { Constants.ValueCodePropertyName, sso },
                    });
            }

            return capabilities;
        }

        private bool IsThirdPartyIdentityProvider()
        {
            return !string.IsNullOrEmpty(_smartIdentityProviderConfiguration.Authority);
        }
    }
}
