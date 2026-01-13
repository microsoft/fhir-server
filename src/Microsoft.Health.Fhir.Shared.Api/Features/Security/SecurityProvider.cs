// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IUrlResolver _urlResolver;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly SmartIdentityProviderConfiguration _smartIdentityProviderConfiguration;

        public SecurityProvider(
            IOptions<SecurityConfiguration> securityConfiguration,
            IHttpClientFactory httpClientFactory,
            ILogger<SecurityConfiguration> logger,
            IUrlResolver urlResolver,
            IModelInfoProvider modelInfoProvider,
            IOptions<SmartIdentityProviderConfiguration> smartIdentityProviderConfiguration)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(securityConfiguration.Value.Authentication.Authority, nameof(securityConfiguration.Value.Authentication.Authority));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(smartIdentityProviderConfiguration?.Value, nameof(smartIdentityProviderConfiguration));

            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _urlResolver = urlResolver;
            _modelInfoProvider = modelInfoProvider;
            _smartIdentityProviderConfiguration = smartIdentityProviderConfiguration.Value;
        }

        public Task BuildAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken)
        {
            if (_securityConfiguration.Enabled)
            {
                try
                {
                    builder.Apply(statement =>
                    {
                        if (_securityConfiguration.EnableAadSmartOnFhirProxy)
                        {
                            AddProxyOAuthSecurityService(statement, RouteNames.AadSmartOnFhirProxyAuthorize, RouteNames.AadSmartOnFhirProxyToken);
                        }
                        else
                        {
                            AddOAuthSecurityService(statement);
                        }
                    });
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "SecurityProvider failed creating a new Capability Statement.");
                    throw;
                }
            }

            return Task.CompletedTask;
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

        private void AddOAuthSecurityService(ListedCapabilityStatement statement)
        {
            ListedRestComponent restComponent = statement.Rest.Server();
            SecurityComponent security = restComponent.Security ?? new SecurityComponent();

            var codableConceptInfo = new CodableConceptInfo();
            security.Service.Add(codableConceptInfo);

            codableConceptInfo.Coding.Add(_modelInfoProvider.Version == FhirSpecification.Stu3
                ? Constants.RestfulSecurityServiceStu3OAuth
                : Constants.RestfulSecurityServiceOAuth);

            var openIdConfigurationUrl = $"{_securityConfiguration.Authentication.Authority}/.well-known/openid-configuration";

            HttpResponseMessage openIdConfigurationResponse;
            using (HttpClient httpClient = _httpClientFactory.CreateClient())
            {
                try
                {
                    openIdConfigurationResponse = httpClient.GetAsync(new Uri(openIdConfigurationUrl)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "There was an exception while attempting to read the OpenId Configuration from \"{OpenIdConfigurationUrl}\".", openIdConfigurationUrl);
                    throw new OpenIdConfigurationException();
                }
            }

            if (openIdConfigurationResponse.IsSuccessStatusCode)
            {
                JObject openIdConfiguration = JObject.Parse(openIdConfigurationResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                string tokenEndpoint, authorizationEndpoint;

                try
                {
                    tokenEndpoint = openIdConfiguration["token_endpoint"].Value<string>();
                    authorizationEndpoint = openIdConfiguration["authorization_endpoint"].Value<string>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "There was an exception while attempting to read the endpoints from \"{OpenIdConfigurationUrl}\".", openIdConfigurationUrl);
                    throw new OpenIdConfigurationException();
                }
                finally
                {
                    openIdConfigurationResponse.Dispose();
                }

                var smartExtensions = CreateSmartExtensions(authorizationEndpoint, tokenEndpoint);
                foreach (var extension in smartExtensions)
                {
                    security.Extension.Add(extension);
                }
            }
            else
            {
                _logger.LogWarning("The OpenId Configuration request from \"{OpenIdConfigurationUrl}\" returned an {StatusCode} status code.", openIdConfigurationUrl, openIdConfigurationResponse.StatusCode);
                openIdConfigurationResponse.Dispose();
                throw new OpenIdConfigurationException();
            }

            restComponent.Security = security;
        }

        private List<JObject> CreateSmartExtensions(string authorizationEndpoint, string tokenEndpoint)
        {
            var extensions = new List<JObject>();
            extensions.Add(GetUrlExtension(authorizationEndpoint, tokenEndpoint));
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

        private List<JObject> GetClientCapabilities()
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

            if (IsThirdPartyIdentityProvider())
            {
                foreach (var client in Constants.SmartCapabilityThirdPartyClients)
                {
                    capabilities.Add(
                        new JObject
                        {
                            { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                            { Constants.ValueCodePropertyName, client },
                        });
                }
            }

            return capabilities;
        }

        private List<JObject> GetContextCapabilities()
        {
            var capabilities = new List<JObject>();
            foreach (var context in Constants.SmartCapabilityContexts)
            {
                capabilities.Add(
                    new JObject
                    {
                        { Constants.UrlPropertyName, Constants.SmartCapabilitiesUriExtension },
                        { Constants.ValueCodePropertyName, context },
                    });
            }

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
