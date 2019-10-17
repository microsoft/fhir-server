// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Newtonsoft.Json.Linq;

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

        public void Build(ICapabilityStatementBuilder builder)
        {
            if (_securityConfiguration.Enabled)
            {
                builder.Update(statement =>
                {
                    if (_securityConfiguration.EnableAadSmartOnFhirProxy)
                    {
                        AddProxyOAuthSecurityService(statement, _urlResolver, RouteNames.AadSmartOnFhirProxyAuthorize, RouteNames.AadSmartOnFhirProxyToken);
                    }
                    else
                    {
                        AddOAuthSecurityService(statement, _securityConfiguration.Authentication.Authority, _httpClientFactory, _logger);
                    }
                });
            }
        }

        private static void AddProxyOAuthSecurityService(ListedCapabilityStatement statement, IUrlResolver urlResolver, string authorizeRouteName, string tokenRouteName)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNullOrWhiteSpace(authorizeRouteName, nameof(authorizeRouteName));
            EnsureArg.IsNotNullOrWhiteSpace(tokenRouteName, nameof(tokenRouteName));

            var restComponent = statement.Rest.Server();
            var security = restComponent.Security ?? new SecurityComponent();

            security.Service.Add(Constants.RestfulSecurityServiceOAuth.ToCoding());

            var tokenEndpoint = urlResolver.ResolveRouteNameUrl(tokenRouteName, null);
            var authorizationEndpoint = urlResolver.ResolveRouteNameUrl(authorizeRouteName, null);

            var smartExtension = new
            {
                Url = Constants.SmartOAuthUriExtension,
                Extension = new[]
                {
                    new
                    {
                        Url = Constants.SmartOAuthUriExtensionToken,
                        Value = tokenEndpoint,
                    },
                    new
                    {
                        Url = Constants.SmartOAuthUriExtensionAuthorize,
                        Value = authorizationEndpoint,
                    },
                },
            };

            security.Extension.Add(smartExtension);
            restComponent.Security = security;
        }

        private static void AddOAuthSecurityService(ListedCapabilityStatement statement, string authority, IHttpClientFactory httpClientFactory, ILogger logger)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(authority, nameof(authority));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));

            var restComponent = statement.Rest.Server();
            var security = restComponent.Security ?? new SecurityComponent();

            security.Service.Add(Constants.RestfulSecurityServiceOAuth.ToCoding());

            var openIdConfigurationUrl = $"{authority}/.well-known/openid-configuration";

            HttpResponseMessage openIdConfigurationResponse;
            using (var httpClient = httpClientFactory.CreateClient())
            {
                try
                {
                    openIdConfigurationResponse = httpClient.GetAsync(new Uri(openIdConfigurationUrl)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"There was an exception while attempting to read the OpenId Configuration from \"{openIdConfigurationUrl}\".");
                    throw new OpenIdConfigurationException();
                }
            }

            if (openIdConfigurationResponse.IsSuccessStatusCode)
            {
                var openIdConfiguration = JObject.Parse(openIdConfigurationResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                string tokenEndpoint, authorizationEndpoint;

                try
                {
                    tokenEndpoint = openIdConfiguration["token_endpoint"].Value<string>();
                    authorizationEndpoint = openIdConfiguration["authorization_endpoint"].Value<string>();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"There was an exception while attempting to read the endpoints from \"{openIdConfigurationUrl}\".");
                    throw new OpenIdConfigurationException();
                }

                var smartExtension = new
                {
                    Url = Constants.SmartOAuthUriExtension,
                    Extension = new[]
                    {
                        new
                        {
                            Url = Constants.SmartOAuthUriExtensionToken,
                            Value = tokenEndpoint,
                        },
                        new
                        {
                            Url = Constants.SmartOAuthUriExtensionAuthorize,
                            Value = authorizationEndpoint,
                        },
                    },
                };

                security.Extension.Add(smartExtension);
            }
            else
            {
                logger.LogWarning($"The OpenId Configuration request from \"{openIdConfigurationUrl}\" returned an {openIdConfigurationResponse.StatusCode} status code.");
                throw new OpenIdConfigurationException();
            }

            restComponent.Security = security;
        }
    }
}
