// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public static class CapabilityStatementExtensions
    {
        public static ListedCapabilityStatement TryAddRestInteraction(this ListedCapabilityStatement statement, ResourceType resourceType, CapabilityStatement.TypeRestfulInteraction interaction)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            statement.BuildRestResourceComponent(resourceType, builder =>
            {
                var hasInteraction = builder
                    .Interaction
                    .FirstOrDefault(x => x.Code == interaction);

                if (hasInteraction == null)
                {
                    builder
                        .Interaction
                        .Add(new CapabilityStatement.ResourceInteractionComponent
                        {
                            Code = interaction,
                        });
                }
            });

            return statement;
        }

        public static ListedCapabilityStatement TryAddRestInteraction(this ListedCapabilityStatement statement, CapabilityStatement.SystemRestfulInteraction interaction)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            var restComponent = statement.Rest.Single();

            if (restComponent.Interaction == null)
            {
                restComponent.Interaction = new List<CapabilityStatement.SystemInteractionComponent>();
            }

            restComponent.Interaction.Add(new CapabilityStatement.SystemInteractionComponent
            {
                Code = interaction,
            });

            return statement;
        }

        public static ListedCapabilityStatement TryAddSearchParams(this ListedCapabilityStatement statement, ResourceType resourceType, IEnumerable<CapabilityStatement.SearchParamComponent> searchParams)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(searchParams, nameof(searchParams));

            statement.BuildRestResourceComponent(resourceType, builder =>
            {
                builder.SearchParam = searchParams.ToList();
            });

            return statement;
        }

        public static ListedCapabilityStatement AddProxyOAuthSecurityService(this ListedCapabilityStatement statement, IUrlResolver urlResolver, string authorizeRouteName, string tokenRouteName)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNullOrWhiteSpace(authorizeRouteName, nameof(authorizeRouteName));
            EnsureArg.IsNotNullOrWhiteSpace(tokenRouteName, nameof(tokenRouteName));

            var restComponent = statement.GetListedRestComponent();
            var security = restComponent.Security ?? new CapabilityStatement.SecurityComponent();

            security.Service.Add(Constants.RestfulSecurityServiceOAuth.ToPoco());
            var tokenEndpoint = urlResolver.ResolveRouteNameUrl(tokenRouteName, null);
            var authorizationEndpoint = urlResolver.ResolveRouteNameUrl(authorizeRouteName, null);

            var smartExtension = new Extension()
            {
                Url = Constants.SmartOAuthUriExtension,
                Extension = new List<Extension>
                {
                    new Extension(Constants.SmartOAuthUriExtensionToken, new FhirUri(tokenEndpoint)),
                    new Extension(Constants.SmartOAuthUriExtensionAuthorize, new FhirUri(authorizationEndpoint)),
                },
            };

            security.Extension.Add(smartExtension);
            restComponent.Security = security;
            return statement;
        }

        public static ListedCapabilityStatement AddOAuthSecurityService(this ListedCapabilityStatement statement, string authority, IHttpClientFactory httpClientFactory, ILogger logger)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(authority, nameof(authority));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));

            var restComponent = statement.GetListedRestComponent();
            var security = restComponent.Security ?? new CapabilityStatement.SecurityComponent();

            security.Service.Add(Constants.RestfulSecurityServiceOAuth.ToPoco());

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

                var smartExtension = new Extension()
                {
                    Url = Constants.SmartOAuthUriExtension,
                    Extension = new List<Extension>
                    {
                        new Extension(Constants.SmartOAuthUriExtensionToken, new FhirUri(tokenEndpoint)),
                        new Extension(Constants.SmartOAuthUriExtensionAuthorize, new FhirUri(authorizationEndpoint)),
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
            return statement;
        }

        internal static ListedRestComponent GetListedRestComponent(this ListedCapabilityStatement statement)
        {
            var restComponent = statement
                .Rest
                .SingleOrDefault();

            if (restComponent == null)
            {
                restComponent = new ListedRestComponent();
                statement.Rest.Add(restComponent);
            }

            return restComponent;
        }
    }
}
