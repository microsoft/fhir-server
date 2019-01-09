// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Newtonsoft.Json.Linq;
using static Hl7.Fhir.Model.OperationOutcome;

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

        public static ListedCapabilityStatement BuildRestResourceComponent(this ListedCapabilityStatement statement, ResourceType resourceType, Action<ListedResourceComponent> componentBuilder)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(componentBuilder, nameof(componentBuilder));

            var restComponent = statement.GetListedRestComponent();

            var restNode = restComponent
                .Resource
                .FirstOrDefault(x => x.Type == resourceType);

            if (restNode == null)
            {
                restNode = new ListedResourceComponent
                {
                    Type = resourceType,
                    Profile = new ResourceReference(ResourceIdentity.Core(resourceType.ToString()).AbsoluteUri),
                };
                restComponent.Resource.Add(restNode);
            }

            componentBuilder(restNode);

            return statement;
        }

        public static ListedCapabilityStatement AddProxyOAuthSecurityService(this ListedCapabilityStatement statement, System.Uri metadataUri)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            var restComponent = statement.GetListedRestComponent();
            var security = restComponent.Security ?? new CapabilityStatement.SecurityComponent();

            security.Service.Add(Constants.RestfulSecurityServiceOAuth);
            var baseurl = metadataUri.Scheme + "://" + metadataUri.Authority;
            var tokenEndpoint = $"{baseurl}/AadSmartOnFhirProxy/token";
            var authorizationEndpoint = $"{baseurl}/AadSmartOnFhirProxy/authorize";

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

            security.Service.Add(Constants.RestfulSecurityServiceOAuth);

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

        public static CapabilityStatement Intersect(this ListedCapabilityStatement system, CapabilityStatement configured, bool strictConfig)
        {
            EnsureArg.IsNotNull(system, nameof(system));
            EnsureArg.IsNotNull(configured, nameof(configured));

            var issues = new List<string>();

            var intersecting = new CapabilityStatement
            {
                // System wide values
                Id = system.Id,
                Url = system.Url?.OriginalString,
                Version = system.Version,
                Name = system.Name,
                Experimental = system.Experimental,
                Publisher = system.Publisher,
                Software = system.Software,
                FhirVersion = system.FhirVersion,
                Contact = new List<ContactDetail> { new ContactDetail { Telecom = system.Telecom?.Select(x => new ContactPoint(x.System, x.Use, x.Value)).ToList() } },

                // Intersections with user configured values
                Kind = system.Kind.IntersectEnum(configured.Kind, issues, "Kind"),
                Status = system.Status.IntersectEnum(configured.Status, issues, "Status"),
                AcceptUnknown = system.AcceptUnknown.IntersectEnum(configured.AcceptUnknown, issues, "AcceptUknown"),
                Format = system.Format?.IntersectList(configured.Format, x => x, issues, "Format"),
            };

            DateTimeOffset cDate;
            if (DateTimeOffset.TryParse(configured.Date, out cDate))
            {
                intersecting.Date = cDate.ToString("o", CultureInfo.InvariantCulture);
            }

            if (system.Rest.Any() && configured.Rest.Any())
            {
                // Only a single rest node is currently supported
                if (system.Rest.Count() > 1 || configured.Rest.Count > 1)
                {
                    throw new NotSupportedException(Core.Resources.CapabilityStatementSingleRestItem);
                }

                var systemRest = system.Rest.Single();
                var configuredRest = configured.Rest.Single();

                var rest = new CapabilityStatement.RestComponent
                {
                    Mode = systemRest.Mode.IntersectEnum(configuredRest.Mode, issues, "Rest.Mode"),
                    Documentation = systemRest.Documentation,
                    Security = systemRest.Security,
                    Interaction = systemRest.Interaction?.IntersectList(configuredRest.Interaction, x => x.Code, issues, $"Rest.Interaction"),
                    SearchParam = systemRest.SearchParam?.IntersectList(configuredRest.SearchParam, x => x.Name, issues, $"Rest.SearchParam"),
                    Operation = systemRest.Operation?.IntersectList(configuredRest.Operation, x => x.Name, issues, $"Rest.Operation"),
                };

                intersecting.Rest.Add(rest);

                var systemComponents = systemRest.Resource.Where(x => configuredRest.Resource.Select(r => r.Type).Contains(x.Type));
                foreach (var systemComponent in systemComponents)
                {
                    var configuredComponent = configuredRest.Resource.Single(x => x.Type == systemComponent.Type);

                    var interaction = new CapabilityStatement.ResourceComponent
                    {
                        // System predefined values
                        Type = systemComponent.Type,

                        // User configurable override
                        Profile = configuredComponent.Profile ?? systemComponent.Profile,

                        // Boolean intersections
                        ReadHistory = systemComponent.ReadHistory.IntersectBool(configuredComponent.ReadHistory, issues, $"Rest.Resource['{systemComponent.Type}'].ReadHistory"),
                        UpdateCreate = systemComponent.UpdateCreate.IntersectBool(configuredComponent.UpdateCreate, issues, $"Rest.Resource['{systemComponent.Type}'.UpdateCreate"),
                        ConditionalCreate = systemComponent.ConditionalCreate.IntersectBool(configuredComponent.ConditionalCreate, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalCreate"),
                        ConditionalUpdate = systemComponent.ConditionalUpdate.IntersectBool(configuredComponent.ConditionalUpdate, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalUpdate"),

                        // List intersections
                        SearchInclude = systemComponent.SearchInclude.IntersectList(configuredComponent.SearchInclude, x => x, issues, $"Rest.Resource['{systemComponent.Type}'].SearchInclude").ToList(),
                        SearchRevInclude = systemComponent.SearchRevInclude.IntersectList(configuredComponent.SearchRevInclude, x => x, issues, $"Rest.Resource['{systemComponent.Type}'].SearchRevInclude").ToList(),
                        Interaction = systemComponent.Interaction.IntersectList(configuredComponent.Interaction, x => x.Code, issues, $"Rest.Resource['{systemComponent.Type}'].Interaction"),
                        ReferencePolicy = systemComponent.ReferencePolicy.IntersectList(configuredComponent.ReferencePolicy, x => x, issues, $"Rest.Resource['{systemComponent.Type}'].ReferencePolicy"),
                        SearchParam = systemComponent.SearchParam.IntersectList(configuredComponent.SearchParam, x => string.Concat(x.Name, x.Type), issues, $"Rest.Resource['{systemComponent.Type}'].SearchParam"),

                        // Listed Enumerations intersections
                        Versioning = systemComponent.Versioning.IntersectEnum(configuredComponent.Versioning, issues, $"Rest.Resource['{systemComponent.Type}'].Versioning"),
                        ConditionalRead = systemComponent.ConditionalRead.IntersectEnum(configuredComponent.ConditionalRead, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalRead"),
                        ConditionalDelete = systemComponent.ConditionalDelete.IntersectEnum(configuredComponent.ConditionalDelete, issues, $"Rest.Resource['{systemComponent.Type}'].ConditionalDelete"),
                    };

                    rest.Resource.Add(interaction);
                }

                rest.Resource = rest.Resource.OrderBy(x => x.Type.ToString()).ToList();
            }

            if (strictConfig && issues.Any())
            {
                throw new UnsupportedConfigurationException(Core.Resources.UnsupportedConfigurationMessage, issues.Select(i => new IssueComponent { Code = IssueType.Exception, Severity = IssueSeverity.Error, Diagnostics = i }).ToArray());
            }

            return intersecting;
        }

        private static ListedRestComponent GetListedRestComponent(this ListedCapabilityStatement statement)
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

        private static bool? IntersectBool(this bool? supported, bool? configured, IList<string> issues, string fieldName)
        {
            if (!supported.HasValue && !configured.HasValue)
            {
                return null;
            }

            if (supported.GetValueOrDefault() == false && configured.GetValueOrDefault())
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidBooleanConfigSetting, fieldName, configured.GetValueOrDefault()));
            }

            return supported.GetValueOrDefault() && configured.GetValueOrDefault();
        }

        /// <summary>
        /// Intersects two enums
        /// </summary>
        /// <typeparam name="T">Type of enum</typeparam>
        /// <param name="supported">The list of supported capabilities.</param>
        /// <param name="configured">The configured capability.</param>
        /// <param name="issues">List of issues found so far</param>
        /// <param name="fieldName">Configured field name</param>
        /// <returns>Valid supported enum</returns>
        private static T? IntersectEnum<T>(this IEnumerable<T> supported, T? configured, IList<string> issues, string fieldName)
            where T : struct, IConvertible
        {
            Debug.Assert(typeof(T).IsEnum, "Generic should be an enum type.");

            if (!configured.HasValue)
            {
                return null;
            }

            if (!supported.Contains(configured.Value))
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidEnumConfigSetting, fieldName, configured.Value, string.Join(",", supported.Select(s => s.ToString(CultureInfo.InvariantCulture)))));
                return supported.LastOrDefault();
            }

            return configured.Value;
        }

        private static List<TElement> IntersectList<TElement, TProperty>(this IEnumerable<TElement> supported, IEnumerable<TElement> configured, Func<TElement, TProperty> selector, IList<string> issues, string fieldName)
        {
            EnsureArg.IsNotNull(supported, nameof(supported));
            EnsureArg.IsNotNull(configured, nameof(configured));
            EnsureArg.IsNotNull(selector, nameof(selector));

            var shouldContain = supported.Select(selector).ToList();
            var config = configured.Where(x => shouldContain.Contains(selector(x))).OrderBy(x => selector(x)?.ToString());

            if (config.Count() != configured.Count())
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidListConfigSetting, fieldName));
            }

            if (config.Select(selector).GroupBy(x => x).Any(x => x.Count() > 1))
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidListConfigDuplicateItem, fieldName));
            }

            return config.ToList();
        }
    }
}
