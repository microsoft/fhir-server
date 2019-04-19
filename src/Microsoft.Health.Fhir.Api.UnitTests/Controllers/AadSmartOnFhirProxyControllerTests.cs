// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Routing;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class AadSmartOnFhirProxyControllerTests
    {
        private readonly SecurityConfiguration _securityConfiguration = new SecurityConfiguration();
        private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
        private readonly ILogger<Core.Configs.SecurityConfiguration> _logger = NullLogger<Core.Configs.SecurityConfiguration>.Instance;
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly FeatureConfiguration _featureConfiguration = new FeatureConfiguration();
        private readonly AadSmartOnFhirProxyController _controller;

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

            var httpMessageHandler = new TestHttpMessageHandler(new HttpResponseMessage()
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(openIdConfiguration),
            });

            var httpClient = new HttpClient(httpMessageHandler);

            _httpClientFactory.CreateClient().Returns(httpClient);

            _controller = new AadSmartOnFhirProxyController(
                Options.Create(_securityConfiguration),
                _httpClientFactory,
                _urlResolver,
                _logger);
        }

        [Theory]
        [InlineData(null, null, null, null, null, null, null)]
        [InlineData("code", null, null, null, null, null, null)]
        [InlineData("code", "clientId", null, null, null, null, null)]
        [InlineData("code", "clientId", "https://testurl.com", null, null, null, null)]
        [InlineData("code", "clientId", "https://testurl.com", "launch", null, null, null)]
        [InlineData("code", "clientId", "https://testurl.com", "launch", "scope", null, null)]
        [InlineData("code", "clientId", "https://testurl.com", "launch", "scope", "state", null)]

        public void GivenIncompleteQueryPamas_WhenAuthorizeRequestAction_ThenRedirectResultReturned(
            string responseType, string clientId, string redirectUri, string launch, string scope, string state, string aud)
        {
            Uri redirect = null;
            if (!string.IsNullOrEmpty(redirectUri))
            {
                redirect = new Uri(redirectUri);
            }

            _urlResolver.ResolveRouteNameUrl(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(redirect);

            var result = _controller.Authorize(responseType, clientId, redirect, launch, scope, state, aud);

            Assert.IsType<RedirectResult>(result);

            var uri = new Uri(((RedirectResult)result).Url);
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
    }
}
