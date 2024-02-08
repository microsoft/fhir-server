// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Providers;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    public class SecurityProvider : IProvideCapability
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly ILogger<SecurityProvider> _logger;
        private readonly IWellKnownConfigurationProvider _wellKnownConfigurationProvider;
        private readonly IUrlResolver _urlResolver;
        private readonly IModelInfoProvider _modelInfoProvider;

        public SecurityProvider(
            IOptions<SecurityConfiguration> securityConfiguration,
            IWellKnownConfigurationProvider wellKnownConfigurationProvider,
            ILogger<SecurityProvider> logger,
            IUrlResolver urlResolver,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(wellKnownConfigurationProvider, nameof(wellKnownConfigurationProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
            _wellKnownConfigurationProvider = wellKnownConfigurationProvider;
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

            Uri authorizationEndpoint = null;
            Uri tokenEndpoint = null;
            Uri registrationEndpoint = null;
            Uri managementEndpoint = null;
            Uri revocationEndpoint = null;
            Uri introspectionEndpoint = null;

            if (_wellKnownConfigurationProvider.IsSmartConfigured())
            {
                // Attempt to fetch the SMART configuration. This may not be configured, in which case we will fall back to the OpenID configuration.
                GetSmartConfigurationResponse smartConfiguration = _wellKnownConfigurationProvider.GetSmartConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
                authorizationEndpoint = smartConfiguration?.AuthorizationEndpoint;
                tokenEndpoint = smartConfiguration?.TokenEndpoint;
                registrationEndpoint = smartConfiguration?.RegistrationEndpoint;
                managementEndpoint = smartConfiguration?.ManagementEndpoint;
                revocationEndpoint = smartConfiguration?.RevocationEndpoint;
                introspectionEndpoint = smartConfiguration?.IntrospectionEndpoint;

                if (authorizationEndpoint != null && tokenEndpoint != null)
                {
                    // The minimum requirements for SMART on FHIR have been satisfied.
                    // Set the restful-security-service code to SMART-on-FHIR.
                    codableConceptInfo.Coding.Add(
                        _modelInfoProvider.Version == FhirSpecification.Stu3
                        ? Constants.RestfulSecurityServiceStu3Smart
                        : Constants.RestfulSecurityServiceSmart);
                }
            }

            if (authorizationEndpoint == null && tokenEndpoint == null)
            {
                // SMART on FHIR is not configured.
                // Set the restful-security-service code to OAuth.
                codableConceptInfo.Coding.Add(
                    _modelInfoProvider.Version == FhirSpecification.Stu3
                    ? Constants.RestfulSecurityServiceStu3OAuth
                    : Constants.RestfulSecurityServiceOAuth);

                // Fallback to OpenID configuration.
                OpenIdConfigurationResponse openIdConfiguration = _wellKnownConfigurationProvider.GetOpenIdConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
                authorizationEndpoint = openIdConfiguration?.AuthorizationEndpoint;
                tokenEndpoint = openIdConfiguration?.TokenEndpoint;
            }

            if (authorizationEndpoint != null && tokenEndpoint != null)
            {
                var extension = new JArray();
                AddExtension(extension, Constants.SmartOAuthUriExtensionToken, tokenEndpoint);
                AddExtension(extension, Constants.SmartOAuthUriExtensionAuthorize, authorizationEndpoint);
                AddExtension(extension, Constants.SmartOAuthUriExtensionRegister, registrationEndpoint);
                AddExtension(extension, Constants.SmartOAuthUriExtensionManage, managementEndpoint);
                AddExtension(extension, Constants.SmartOAuthUriExtensionRevoke, revocationEndpoint);
                AddExtension(extension, Constants.SmartOAuthUriExtensionIntrospect, introspectionEndpoint);

                security.Extension.Add(JObject.FromObject(new { url = Constants.SmartOAuthUriExtension, extension }));
            }
            else
            {
                throw new OpenIdConfigurationException();
            }

            restComponent.Security = security;
        }

        private static void AddExtension(JArray extension, string key, Uri value)
        {
            if (!string.IsNullOrWhiteSpace(key) && value != null)
            {
                var entry = JObject.FromObject(new { url = key, valueUri = value });
                extension.Add(entry);
            }
        }
    }
}
