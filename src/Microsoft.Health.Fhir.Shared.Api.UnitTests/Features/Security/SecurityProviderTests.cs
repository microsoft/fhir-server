// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Api.UnitTests.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class SecurityProviderTests
    {
        private const string OpenIdAuthorizationEndpointUri = "http://test/openid/authorize";
        private const string OpenIdTokenEndpointUri = "http://test/openid/token";
        private const string SmartAuthorizationEndpointUri = "http://test/smart/authorize";
        private const string SmartTokenEndpointUri = "http://test/smart/token";
        private const string ThirdPartyAuthorizeRootUri = "http://test/thirdparty";
        private const string IntrospectionEndpointUri = "http://test/thirdparty/introspection";
        private const string ManagementEndpointUri = "http://test/thirdparty/management";
        private const string RevocationEndpointUri = "http://test/thirdparty/revocation";

        private readonly SecurityProvider _provider;
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IUrlResolver _urlResolver;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly SmartIdentityProviderConfiguration _smartIdentityProviderConfiguration;

        public SecurityProviderTests()
        {
            _securityConfiguration = new SecurityConfiguration()
            {
                Authentication = new AuthenticationConfiguration()
                {
                    Authority = SmartAuthorizationEndpointUri,
                },
            };

            _httpClientFactory = Substitute.For<IHttpClientFactory>();
            _httpClientFactory.CreateClient().Returns(new HttpClient());

            _urlResolver = Substitute.For<IUrlResolver>();
            _urlResolver.ResolveRouteNameUrl(
                Arg.Is<string>(x => string.Equals(x, RouteNames.AadSmartOnFhirProxyAuthorize, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IDictionary<string, object>>())
                .Returns(new Uri(SmartAuthorizationEndpointUri));
            _urlResolver.ResolveRouteNameUrl(
                Arg.Is<string>(x => string.Equals(x, RouteNames.AadSmartOnFhirProxyToken, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IDictionary<string, object>>())
                .Returns(new Uri(SmartTokenEndpointUri));

            _modelInfoProvider = Substitute.For<IModelInfoProvider>();
            _modelInfoProvider.Version.Returns(FhirSpecification.R4);

            _smartIdentityProviderConfiguration = new SmartIdentityProviderConfiguration();
            _provider = new SecurityProvider(
                Options.Create(_securityConfiguration),
                _httpClientFactory,
                Substitute.For<ILogger<SecurityConfiguration>>(),
                _urlResolver,
                _modelInfoProvider,
                Options.Create(_smartIdentityProviderConfiguration));
        }

        [Theory]
        [InlineData(true, FhirSpecification.R4)]
        [InlineData(true, FhirSpecification.Stu3)]
        [InlineData(false, FhirSpecification.R4)]
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenCapabilityStatementShouldHaveSmartEndpoints(
            bool enabled,
            FhirSpecification specification)
        {
            _securityConfiguration.Enabled = enabled;
            _securityConfiguration.EnableAadSmartOnFhirProxy = true;
            _modelInfoProvider.Version.Returns(specification);

            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            var builder = Substitute.For<ICapabilityStatementBuilder>();
            builder
                .When(x => x.Apply(Arg.Any<Action<ListedCapabilityStatement>>()))
                .Do(
                    x =>
                    {
                        var action = x.Arg<Action<ListedCapabilityStatement>>();
                        action.Invoke(capabilityStatement);
                    });
            await _provider.BuildAsync(builder, CancellationToken.None);

            Validate(
                enabled,
                restComponent,
                specification,
                _smartIdentityProviderConfiguration,
                SmartAuthorizationEndpointUri,
                SmartTokenEndpointUri);
            _urlResolver.Received(enabled ? 1 : 0).ResolveRouteNameUrl(
                Arg.Is<string>(x => string.Equals(x, RouteNames.AadSmartOnFhirProxyAuthorize, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IDictionary<string, object>>());
            _urlResolver.Received(enabled ? 1 : 0).ResolveRouteNameUrl(
                Arg.Is<string>(x => string.Equals(x, RouteNames.AadSmartOnFhirProxyToken, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<IDictionary<string, object>>());
        }

        [Theory]
        [InlineData(true, FhirSpecification.R4, HttpStatusCode.OK, true, false)] // R4 success - Security populated
        [InlineData(true, FhirSpecification.Stu3, HttpStatusCode.OK, true, false)] // Stu3 success - Security populated
        [InlineData(false, FhirSpecification.R4, HttpStatusCode.OK, true, false)] // Disabled - Security skipped
        [InlineData(true, FhirSpecification.R4, HttpStatusCode.BadRequest, true, false)] // Failed response from openId well-known endpoint - Security not populated
        [InlineData(true, FhirSpecification.R4, HttpStatusCode.OK, false, false)] // Bad response content from openId well-known endpoint - Security not populated
        [InlineData(true, FhirSpecification.R4, HttpStatusCode.OK, true, true)] // Exception on calling openId well-known endpoint - Security not populated
        public async Task GivenConfiguration_WhenBuildingCapabilityStatement_ThenCapabilityStatementShouldHaveOAuthEndpoints(
            bool enabled,
            FhirSpecification specification,
            HttpStatusCode statusCode,
            bool validContent,
            bool throwException)
        {
            _securityConfiguration.Enabled = enabled;
            _securityConfiguration.EnableAadSmartOnFhirProxy = false;
            _modelInfoProvider.Version.Returns(specification);

            var responseBody = new
            {
                authorization_endpoint = OpenIdAuthorizationEndpointUri,
                token_endpoint = OpenIdTokenEndpointUri,
            };

            var responseMessage = new HttpResponseMessage()
            {
                StatusCode = statusCode,
                Content = new StringContent(
                    validContent ? JObject.FromObject(responseBody).ToString() : "{}"),
            };

            var httpMessageHandler = new TestHttpMessageHandler(
                responseMessage,
                throwException ? new Exception("something went wrong") : null);
            var httpClient = new HttpClient(httpMessageHandler);
            _httpClientFactory.CreateClient().Returns(httpClient);

            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            var builder = Substitute.For<ICapabilityStatementBuilder>();
            builder
                .When(x => x.Apply(Arg.Any<Action<ListedCapabilityStatement>>()))
                .Do(
                    x =>
                    {
                        var action = x.Arg<Action<ListedCapabilityStatement>>();
                        action.Invoke(capabilityStatement);
                    });

            try
            {
                await _provider.BuildAsync(builder, CancellationToken.None);
                Assert.True(IsSuccessStatusCode(statusCode) && validContent && !throwException);

                Validate(
                    enabled,
                    restComponent,
                    specification,
                    _smartIdentityProviderConfiguration,
                    OpenIdAuthorizationEndpointUri,
                    OpenIdTokenEndpointUri);
            }
            catch (OpenIdConfigurationException)
            {
                Assert.True(!IsSuccessStatusCode(statusCode) || !validContent || throwException);
            }

            _httpClientFactory.Received(enabled ? 1 : 0).CreateClient();
        }

        [Theory]
        [InlineData(null, null, null, null)]
        [InlineData(ThirdPartyAuthorizeRootUri, null, null, null)]
        [InlineData(ThirdPartyAuthorizeRootUri, IntrospectionEndpointUri, null, null)]
        [InlineData(null, IntrospectionEndpointUri, null, null)]
        [InlineData(ThirdPartyAuthorizeRootUri, null, ManagementEndpointUri, null)]
        [InlineData(ThirdPartyAuthorizeRootUri, null, null, RevocationEndpointUri)]
        [InlineData(ThirdPartyAuthorizeRootUri, IntrospectionEndpointUri, ManagementEndpointUri, RevocationEndpointUri)]
        public async Task GivenConfigurations_WhenBuildingCapabilityStatement_ThenCapabilityStatementShouldHaveSmartCapabilities(
            string authority,
            string introspection,
            string management,
            string revocation)
        {
            _securityConfiguration.Enabled = true;
            _securityConfiguration.EnableAadSmartOnFhirProxy = true;

            _smartIdentityProviderConfiguration.Authority = authority;
            _smartIdentityProviderConfiguration.Introspection = introspection;
            _smartIdentityProviderConfiguration.Management = management;
            _smartIdentityProviderConfiguration.Revocation = revocation;

            var authorize = !string.IsNullOrEmpty(authority) ? $"{authority.TrimEnd('/')}/{Constants.SmartOAuthUriExtensionAuthorize}" : SmartAuthorizationEndpointUri;
            var token = !string.IsNullOrEmpty(authority) ? $"{authority.TrimEnd('/')}/{Constants.SmartOAuthUriExtensionToken}" : SmartTokenEndpointUri;
            var restComponent = new ListedRestComponent()
            {
                Mode = ListedCapabilityStatement.ServerMode,
            };

            var capabilityStatement = new ListedCapabilityStatement();
            capabilityStatement.Rest.Add(restComponent);

            var builder = Substitute.For<ICapabilityStatementBuilder>();
            builder
                .When(x => x.Apply(Arg.Any<Action<ListedCapabilityStatement>>()))
                .Do(
                    x =>
                    {
                        var action = x.Arg<Action<ListedCapabilityStatement>>();
                        action.Invoke(capabilityStatement);
                    });
            await _provider.BuildAsync(builder, CancellationToken.None);

            Validate(
                true,
                restComponent,
                FhirSpecification.R4,
                _smartIdentityProviderConfiguration,
                authorize,
                token,
                introspection,
                management,
                revocation);
        }

        private static bool IsSuccessStatusCode(HttpStatusCode statusCode)
        {
            return ((int)statusCode >= 200) && ((int)statusCode <= 299);
        }

        private static void Validate(
            bool enabled,
            ListedRestComponent component,
            FhirSpecification specification,
            SmartIdentityProviderConfiguration configuration,
            string authorize,
            string token,
            string introspection = null,
            string management = null,
            string revocation = null)
        {
            if (enabled)
            {
                Assert.NotNull(component?.Security?.Extension);
                Assert.NotNull(component?.Security?.Service);

                ValidateOAuthUriExtension(
                    component.Security.Extension,
                    authorize,
                    token,
                    introspection,
                    management,
                    revocation);
                ValidateSecurity(
                    component.Security.Service,
                    specification);
                ValidateSmartCapabilities(
                    configuration,
                    component.Security.Extension);
            }
            else
            {
                Assert.Null(component?.Security);
            }
        }

        private static void ValidateOAuthUriExtension(
            ICollection<JObject> extensions,
            string authorize,
            string token,
            string introspection = null,
            string management = null,
            string revocation = null)
        {
            var ext = extensions
                .Where(x => string.Equals(x[Constants.UrlPropertyName]?.Value<string>(), Constants.SmartOAuthUriExtension, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            Assert.NotNull(ext);

            var urls = ext[Constants.ExtensionPropertyName]?
                .ToDictionary(x => x[Constants.UrlPropertyName]?.ToString(), x => x[Constants.ValueUriPropertyName]?.ToString());
            Assert.True(urls.ContainsKey(Constants.SmartOAuthUriExtensionAuthorize));
            Assert.Contains(
                urls,
                x =>
                {
                    return string.Equals(x.Key, Constants.SmartOAuthUriExtensionAuthorize, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Value, authorize, StringComparison.OrdinalIgnoreCase);
                });

            Assert.True(urls.ContainsKey(Constants.SmartOAuthUriExtensionToken));
            Assert.Contains(
                urls,
                x =>
                {
                    return string.Equals(x.Key, Constants.SmartOAuthUriExtensionToken, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Value, token, StringComparison.OrdinalIgnoreCase);
                });

            if (!string.IsNullOrEmpty(introspection))
            {
                Assert.Contains(
                    urls,
                    x =>
                    {
                        return string.Equals(x.Key, Constants.SmartOAuthUriExtensionIntrospection, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Value, introspection, StringComparison.OrdinalIgnoreCase);
                    });
            }
            else
            {
                Assert.False(urls.ContainsKey(Constants.SmartOAuthUriExtensionIntrospection));
            }

            if (!string.IsNullOrEmpty(management))
            {
                Assert.Contains(
                    urls,
                    x =>
                    {
                        return string.Equals(x.Key, Constants.SmartOAuthUriExtensionManagement, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Value, management, StringComparison.OrdinalIgnoreCase);
                    });
            }
            else
            {
                Assert.False(urls.ContainsKey(Constants.SmartOAuthUriExtensionManagement));
            }

            if (!string.IsNullOrEmpty(revocation))
            {
                Assert.Contains(
                    urls,
                    x =>
                    {
                        return string.Equals(x.Key, Constants.SmartOAuthUriExtensionRevocation, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Value, revocation, StringComparison.OrdinalIgnoreCase);
                    });
            }
            else
            {
                Assert.False(urls.ContainsKey(Constants.SmartOAuthUriExtensionRevocation));
            }
        }

        private static void ValidateSecurity(
            ICollection<CodableConceptInfo> security,
            FhirSpecification specification)
        {
            Assert.Single(security);

            var coding = security.FirstOrDefault()?.Coding?.FirstOrDefault();
            Assert.NotNull(coding);

            if (specification == FhirSpecification.Stu3)
            {
                Assert.Equal(Constants.RestfulSecurityServiceStu3OAuth.System, coding.System, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(Constants.RestfulSecurityServiceStu3OAuth.Code, coding.Code, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Equal(Constants.RestfulSecurityServiceOAuth.System, coding.System, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(Constants.RestfulSecurityServiceOAuth.Code, coding.Code, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void ValidateSmartCapabilities(
            SmartIdentityProviderConfiguration configuration,
            ICollection<JObject> extensions)
        {
            var actual = extensions
                .Where(x => string.Equals(x[Constants.UrlPropertyName]?.Value<string>(), Constants.SmartCapabilitiesUriExtension, StringComparison.OrdinalIgnoreCase))
                .Select(x => x[Constants.ValueCodePropertyName].ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.NotNull(actual);

            var capabilities = new List<string>(Constants.SmartCapabilityClients
                .Concat(Constants.SmartCapabilityAdditional)
                .Concat(Constants.SmartCapabilityLaunches)
                .Concat(Constants.SmartCapabilityPermissions)
                .Concat(Constants.SmartCapabilitySSOs));
            if (!string.IsNullOrEmpty(configuration?.Authority))
            {
                capabilities.AddRange(Constants.SmartCapabilityThirdPartyContexts);
            }

            var expected = capabilities.ToHashSet();
            Assert.True(actual.SetEquals(expected));
        }
    }
}
