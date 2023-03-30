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

        public SecurityProvider(
            IOptions<SecurityConfiguration> securityConfiguration,
            IHttpClientFactory httpClientFactory,
            ILogger<SecurityConfiguration> logger,
            IUrlResolver urlResolver,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(securityConfiguration.Value.Authentication.Authority, nameof(securityConfiguration.Value.Authentication.Authority));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _urlResolver = urlResolver;
            _modelInfoProvider = modelInfoProvider;
        }

        public void Build(ICapabilityStatementBuilder builder)
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

            var smartExtension = new
            {
                url = Constants.SmartOAuthUriExtension,
                extension = new[]
                {
                    new
                    {
                        url = Constants.SmartOAuthUriExtensionToken,
                        valueUri = tokenEndpoint,
                    },
                    new
                    {
                        url = Constants.SmartOAuthUriExtensionAuthorize,
                        valueUri = authorizationEndpoint,
                    },
                },
            };

            security.Extension.Add(JObject.FromObject(smartExtension));
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

                var smartExtension = new
                {
                    url = Constants.SmartOAuthUriExtension,
                    extension = new[]
                    {
                        new
                        {
                            url = Constants.SmartOAuthUriExtensionToken,
                            valueUri = tokenEndpoint,
                        },
                        new
                        {
                            url = Constants.SmartOAuthUriExtensionAuthorize,
                            valueUri = authorizationEndpoint,
                        },
                    },
                };

                security.Extension.Add(JObject.FromObject(smartExtension));
            }
            else
            {
                _logger.LogWarning("The OpenId Configuration request from \"{OpenIdConfigurationUrl}\" returned an {StatusCode} status code.", openIdConfigurationUrl, openIdConfigurationResponse.StatusCode);
                openIdConfigurationResponse.Dispose();
                throw new OpenIdConfigurationException();
            }

            restComponent.Security = security;
        }
    }
}
