// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SMARTProxy.Configuration;
using SMARTProxy.Filters;

namespace SMARTProxy.UnitTests.Filters
{
    public class AuthorizeInputFilterTests
    {
        private static SMARTProxyConfig config = new SMARTProxyConfig()
        {
            TenantId = "xxxx-xxxx-xxxx-xxxx",
        };

        [Fact]
        public async Task GivenAPkceGetAuthorizeRequest_WhenFilterExecuted_ThenRequestIsChangedToProperRedirect()
        {
            var logger = Substitute.For<ILogger<AuthorizeInputFilter>>();
            var filter = new AuthorizeInputFilter(logger, config);

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Get;
            context.Request.RequestUri = new Uri(string.Concat(
                "http://localhost/authorize",
                "?response_type=code&client_id=xxxx-xxxxx-xxxxx-xxxxx&redirect_uri=http://localhost&scope=patient/Patient.read fhir user openid",
                "&state=123&aud=https://workspace-fhir.fhir.azurehealthcareapis.com&code_challenge_method=S256&code_challenge=ECgEuvKylvpiOS9pF2pfu5NKoBErrx8fAWdneyiPT2E"));

            await filter.ExecuteAsync(context);

            Assert.Equal(HttpStatusCode.Redirect, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/authorize", context.Request.RequestUri.AbsolutePath);

            Assert.Equal(1, context.Headers.Count(x => x.Name == "Location"));
            Assert.Equal(1, context.Headers.Count(x => x.Name == "Origin"));
        }

        [Fact]
        public async Task GivenAPkcePostAuthorizeRequest_WhenFilterExecuted_ThenRequestIsChangedToProperRedirect()
        {
            var logger = Substitute.For<ILogger<AuthorizeInputFilter>>();
            var filter = new AuthorizeInputFilter(logger, config);

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Post;
            context.Request.RequestUri = new Uri("http://localhost/authorize");
            context.Request.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("response_type", "code"),
                new KeyValuePair<string, string>("client_id", "xxxx-xxxxx-xxxxx-xxxxx"),
                new KeyValuePair<string, string>("redirect_uri", "http://localhost"),
                new KeyValuePair<string, string>("scope", "patient/Patient.read fhir user openid"),
                new KeyValuePair<string, string>("state", "567"),
                new KeyValuePair<string, string>("aud", "https://workspace-fhir.fhir.azurehealthcareapis.com"),
                new KeyValuePair<string, string>("code_challenge_method", "S256"),
                new KeyValuePair<string, string>("code_challenge", "test"),
            });

            await filter.ExecuteAsync(context);

            Assert.Equal(HttpStatusCode.Redirect, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/authorize", context.Request.RequestUri.AbsolutePath);

            Assert.Equal(1, context.Headers.Count(x => x.Name == "Location"));
            Assert.Equal(1, context.Headers.Count(x => x.Name == "Origin"));
        }

        [Fact]
        public async Task GivenAnImplicitGetAuthorizeRequest_WhenFilterExecuted_ThenRequestIsChangedToProperRedirect()
        {
            var logger = Substitute.For<ILogger<AuthorizeInputFilter>>();
            var filter = new AuthorizeInputFilter(logger, config);

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Get;
            context.Request.RequestUri = new Uri(string.Concat(
                "http://localhost/authorize",
                "?response_type=code&client_id=xxxx-xxxxx-xxxxx-xxxxx&redirect_uri=http://localhost&scope=patient/Patient.read fhir user openid",
                "&state=123&aud=https://workspace-fhir.fhir.azurehealthcareapis.com"));

            await filter.ExecuteAsync(context);

            Assert.Equal(HttpStatusCode.Redirect, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/authorize", context.Request.RequestUri.AbsolutePath);

            Assert.Equal(1, context.Headers.Count(x => x.Name == "Location"));
            Assert.Equal(0, context.Headers.Count(x => x.Name == "Origin"));
        }

        [Fact]
        public async Task GivenAnImplicitPostAuthorizeRequest_WhenFilterExecuted_ThenRequestIsChangedToProperRedirect()
        {
            var logger = Substitute.For<ILogger<AuthorizeInputFilter>>();
            var filter = new AuthorizeInputFilter(logger, config);

            OperationContext context = new();
            context.Request = new HttpRequestMessage();
            context.Request.Method = HttpMethod.Post;
            context.Request.RequestUri = new Uri("http://localhost/authorize");
            context.Request.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("response_type", "code"),
                new KeyValuePair<string, string>("client_id", "xxxx-xxxxx-xxxxx-xxxxx"),
                new KeyValuePair<string, string>("redirect_uri", "http://localhost"),
                new KeyValuePair<string, string>("scope", "patient/Patient.read fhir user openid"),
                new KeyValuePair<string, string>("state", "567"),
                new KeyValuePair<string, string>("aud", "https://workspace-fhir.fhir.azurehealthcareapis.com"),
            });

            await filter.ExecuteAsync(context);

            Assert.Equal(HttpStatusCode.Redirect, context.StatusCode);
            Assert.Equal("login.microsoftonline.com", context.Request.RequestUri.Host);
            Assert.Equal($"/{config.TenantId}/oauth2/v2.0/authorize", context.Request.RequestUri.AbsolutePath);

            Assert.Equal(1, context.Headers.Count(x => x.Name == "Location"));
            Assert.Equal(0, context.Headers.Count(x => x.Name == "Origin"));
        }
    }
}
