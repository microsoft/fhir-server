// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class AadSmartOnFhirProxyControllerTests
    {
        private readonly SecurityConfiguration _securityConfiguration = new SecurityConfiguration();
        private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
        private readonly ILogger<Core.Configs.SecurityConfiguration> _logger = NullLogger<Core.Configs.SecurityConfiguration>.Instance;
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly FeatureConfiguration _featureConfiguration = new FeatureConfiguration();
        private readonly AadSmartOnFhirProxyController _controller;
        private TestHttpMessageHandler _httpMessageHandler;

        public AadSmartOnFhirProxyControllerTests()
        {
            _securityConfiguration.EnableAadSmartOnFhirProxy = true;
            _securityConfiguration.Enabled = true;
            _securityConfiguration.Authorization = new AuthorizationConfiguration
            {
                Enabled = true,
            };
            _securityConfiguration.Authentication = new AuthenticationConfiguration
            {
                Audience = "testaudience",
                Authority = "http://testauthority.com/v2.0",
            };

            string openIdConfiguration;
            using (StreamReader r = new StreamReader(Assembly.GetExecutingAssembly().
                GetManifestResourceStream("Microsoft.Health.Fhir.Api.UnitTests.Controllers.openid-configuration.json")))
            {
                openIdConfiguration = r.ReadToEnd();
            }

            _httpMessageHandler = new TestHttpMessageHandler(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(openIdConfiguration),
            });

            var httpClient = new HttpClient(_httpMessageHandler);

            _httpClientFactory.CreateClient().Returns(httpClient);

            _controller = new AadSmartOnFhirProxyController(
                Options.Create(_securityConfiguration),
                _httpClientFactory,
                _urlResolver,
                _logger);
        }

        [Theory]
        [InlineData(null, null, null, null, null, null, null)]
        [InlineData(null, null, null, "launch", null, null, null)]
        [InlineData("code", null, null, "launch", null, null, null)]
        [InlineData("code", "clientId", null, "launch", null, null, null)]
        [InlineData("code", null, null, null, null, null, null)]
        [InlineData("code", "clientId", null, null, null, null, null)]
        [InlineData("code", "clientId", "https://testurl.com", null, null, null, null)]
        [InlineData("code", "clientId", "https://testurl.com", "launch", null, null, null)]
        [InlineData("code", "clientId", "https://testurl.com", "launch", "scope", null, null)]
        [InlineData("code", "clientId", "https://testurl.com", "launch", "scope", "state", null)]
        public void GivenIncompleteQueryParams_WhenAuthorizeRequestAction_ThenRedirectResultReturned(
            string responseType, string clientId, string redirectUri, string launch, string scope, string state, string aud)
        {
            Uri redirect = null;
            if (!string.IsNullOrEmpty(redirectUri))
            {
                redirect = new Uri(redirectUri);
            }

            _urlResolver.ResolveRouteNameUrl(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(redirect);

            var result = _controller.Authorize(responseType, clientId, redirect, launch, scope, state, aud);

            var redirectResult = result as RedirectResult;
            Assert.NotNull(redirectResult);

            var uri = new Uri(redirectResult.Url);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);

            Assert.Equal(responseType, queryParams["response_type"]);
            Assert.Equal(clientId, queryParams["client_id"]);

            if (!string.IsNullOrEmpty(redirectUri))
            {
                Assert.Contains(redirect.Host, uri.AbsoluteUri);
            }

            if (!string.IsNullOrEmpty(scope))
            {
                Assert.NotNull(queryParams["scope"]);
            }

            if (!string.IsNullOrEmpty(state))
            {
                Assert.NotNull(queryParams["state"]);
            }
        }

        [Fact]
        public void GivenMissingAudParamToV1AAd_WhenAuthorizeRequestAction_ThenRedirectResultReturned()
        {
            _securityConfiguration.Authentication = new AuthenticationConfiguration
            {
                Audience = "testaudience",
                Authority = "http://testauthority.com/",
            };

            var v1Controller = new AadSmartOnFhirProxyController(
                Options.Create(_securityConfiguration),
                _httpClientFactory,
                _urlResolver,
                _logger);

            var redirect = new Uri("http://test.uri");

            _urlResolver.ResolveRouteNameUrl(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(redirect);

            var result = _controller.Authorize("code", "clientId", redirect, "launch", null, "state", null);

            var redirectResult = result as RedirectResult;
            Assert.NotNull(redirectResult);

            var uri = new Uri(redirectResult.Url);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);

            Assert.Null(queryParams["resource"]);
        }

        [Theory]
        [MemberData(nameof(GetParamsDataForGivenInvalidQueryParams_RedirectUrlCodeStateSession))]
        public void GivenInvalidQueryParams_WhenCallbackRequestAction_ThenBadRequestExceptionThrown(
            string redirectUrl, string code, string state, string sessionState)
        {
            Assert.Throws<AadSmartOnFhirProxyBadRequestException>(() => _controller.Callback(redirectUrl, code, state, sessionState, null, null));
        }

        public static IEnumerable<object[]> GetParamsDataForGivenInvalidQueryParams_RedirectUrlCodeStateSession()
        {
            var foo = Convert.ToBase64String(Encoding.UTF8.GetBytes("foo"));
            var testUrl = Convert.ToBase64String(Encoding.UTF8.GetBytes("http://test.url"));

            yield return new object[] { foo, null, null, null };
            yield return new object[] { testUrl, null, null, null };
            yield return new object[] { testUrl, "code", null, null };
            yield return new object[] { testUrl, "code", "state", null };
        }

        [Theory]
        [InlineData(null, null, null, null, null)]
        [InlineData("authorization_code", null, null, null, null)]
        [InlineData("authorization_code", "clientId", null, null, null)]
        [InlineData("authorization_code", "clientId", "clientSecret", null, null)]
        [InlineData("authorization_code", "clientId", "clientSecret", "InvalidCode", null)]
        [MemberData(nameof(GetParamsDataForGivenInvalidQueryParams_TokenCompoundCode))]
        public async Task GivenInvalidQueryParams_WhenTokenRequestAction_ThenBadRequestExceptionThrown(
            string grantType, string clientId, string clientSecret, string compoundCode, string redirectUriString)
        {
            Uri redirectUri = null;
            if (redirectUriString != null)
            {
                redirectUri = new Uri(redirectUriString);
            }

            _urlResolver.ResolveRouteNameUrl(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(redirectUri);

            _httpMessageHandler.Response = new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{ \"client_id\" : \"client id\", \"scope\" : \"scope\"}"),
            };

            await Assert.ThrowsAsync<AadSmartOnFhirProxyBadRequestException>(() => _controller.Token(grantType, compoundCode, redirectUri, clientId, clientSecret));
        }

        public static IEnumerable<object[]> GetParamsDataForGivenInvalidQueryParams_TokenCompoundCode()
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("{ \"code\" : \"foo\" }"));
            yield return new object[] { "authorization_code", "clientId", "clientSecret", encoded, null };
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("user_impersonation test.scope", "user_impersonation test.scope")]
        [InlineData("patient$Observation.read", "patient/Observation.read")]
        [InlineData("patient$Observation.read patient$Encounter.read", "patient/Observation.read patient/Encounter.read")]
        public async Task GivenScopesInTokenResponse_WhenTokenRequestAction_ThenCorrectScopesReturned(string tokenScopes, string expectedScopes)
        {
            string grantType = "authorization_code";
            string clientId = "1234";
            string clientStringPlaceHolder = "XYZ";
            string compoundCode = "eyAiY29kZSIgOiAiZm9vIiB9";
            Uri redirectUri = new Uri("https://localhost");

            _urlResolver.ResolveRouteNameUrl(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(redirectUri);

            JObject content = new JObject();
            content["access_token"] = "xyz";
            if (tokenScopes != null)
            {
                content["scope"] = tokenScopes;
            }

            _httpMessageHandler.Response = new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(content.ToString(Formatting.None)),
            };

            var result = await _controller.Token(grantType, compoundCode, redirectUri, clientId, clientStringPlaceHolder) as ContentResult;
            Assert.NotNull(result);

            var resultJson = JObject.Parse(result.Content);

            Assert.Equal(expectedScopes, resultJson["scope"]?.ToString());
        }
    }
}
