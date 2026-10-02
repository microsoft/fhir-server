// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class AadSmartOnFhirProxyMvcTests
    {
        private const string Authority = "https://authority.example/v2.0";
        private const string AuthorizeEndpoint = "https://authority.example/authorize";
        private const string TokenEndpoint = "https://authority.example/token";
        private const string RedirectUri = "https://client.example/callback";
        private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
        private readonly IAuditHelper _auditHelper = Substitute.For<IAuditHelper>();
        private int? _auditedStatusCode;

        [Theory]
        [InlineData("authorize", null)]
        [InlineData("authorize", "")]
        [InlineData("authorize", "invalid-authority")]
        [InlineData("authorize", Authority)]
        [InlineData("callback", null)]
        [InlineData("callback", "")]
        [InlineData("callback", "invalid-authority")]
        [InlineData("callback", Authority)]
        [InlineData("token", null)]
        [InlineData("token", "")]
        [InlineData("token", "invalid-authority")]
        [InlineData("token", Authority)]
        public async Task GivenProxyDisabled_WhenRequestUsesMvc_ThenUnauthorizedIsAuditedWithoutDiscovery(string action, string authority)
        {
            using var handler = new DiscoveryHttpMessageHandler(failDiscovery: true);
            using var server = CreateHost(enableProxy: false, authority, handler);
            using var client = server.GetTestClient();
            using var request = CreateRequest(action);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(handler.RequestedUris);
            _httpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
            AssertAudited(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData("authorize", HttpStatusCode.Redirect, null)]
        [InlineData("callback", HttpStatusCode.Redirect, null)]
        [InlineData("token", HttpStatusCode.OK, "client_credentials")]
        [InlineData("token", HttpStatusCode.OK, "authorization_code")]
        public async Task GivenProxyEnabled_WhenRequestUsesMvc_ThenDiscoveryAndActionExecuteAndAreAudited(string action, HttpStatusCode expectedStatus, string grantType)
        {
            using var handler = new DiscoveryHttpMessageHandler(failDiscovery: false);
            using var server = CreateHost(enableProxy: true, Authority, handler);
            using var client = server.GetTestClient();
            using var request = CreateRequest(action, grantType);

            using var response = await client.SendAsync(request);

            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(new Uri($"{Authority}/.well-known/openid-configuration"), handler.RequestedUris[0]);
            if (action == "token")
            {
                Assert.Equal(2, handler.RequestedUris.Count);
                Assert.Equal(new Uri(TokenEndpoint), handler.RequestedUris[1]);
                var content = await response.Content.ReadAsStringAsync();
                if (grantType == "authorization_code")
                {
                    var tokenResponse = JObject.Parse(content);
                    Assert.Equal("test-token", tokenResponse["access_token"]?.Value<string>());
                    Assert.Equal("test-client", tokenResponse["client_id"]?.Value<string>());
                    Assert.Equal("test-patient", tokenResponse["patient"]?.Value<string>());
                }
                else
                {
                    Assert.Equal("{\"access_token\":\"test-token\"}", content);
                }
            }
            else
            {
                Assert.Single(handler.RequestedUris);
                Assert.StartsWith(action == "authorize" ? AuthorizeEndpoint : RedirectUri, response.Headers.Location.AbsoluteUri);
            }

            AssertAudited(expectedStatus);
        }

        [Theory]
        [InlineData("authorize")]
        [InlineData("callback")]
        [InlineData("token")]
        public async Task GivenProxyEnabledAndDiscoveryUnavailable_WhenRequestUsesMvc_ThenDiscoveryErrorIsPreserved(string action)
        {
            using var handler = new DiscoveryHttpMessageHandler(failDiscovery: true);
            using var server = CreateHost(enableProxy: true, Authority, handler);
            using var client = server.GetTestClient();
            using var request = CreateRequest(action);

            await Assert.ThrowsAsync<OpenIdConfigurationException>(() => client.SendAsync(request));

            Assert.Equal(new Uri($"{Authority}/.well-known/openid-configuration"), Assert.Single(handler.RequestedUris));
        }

        [Theory]
        [InlineData("callback/not-base64", true, HttpStatusCode.BadRequest)]
        [InlineData("token", true, HttpStatusCode.BadRequest)]
        [InlineData("callback/not-base64", false, HttpStatusCode.Unauthorized)]
        [InlineData("token", false, HttpStatusCode.Unauthorized)]
        public async Task GivenInvalidRequest_WhenRequestUsesMvc_ThenFeatureGatePrecedesActionValidationAndResultIsAudited(string path, bool enableProxy, HttpStatusCode expectedStatus)
        {
            using var handler = new DiscoveryHttpMessageHandler(failDiscovery: !enableProxy);
            using var server = CreateHost(enableProxy, Authority, handler);
            using var client = server.GetTestClient();
            using var request = new HttpRequestMessage(path == "token" ? HttpMethod.Post : HttpMethod.Get, $"/AadSmartOnFhirProxy/{path}");

            using var response = await client.SendAsync(request);

            Assert.Equal(expectedStatus, response.StatusCode);
            if (enableProxy)
            {
                Assert.Single(handler.RequestedUris);
            }
            else
            {
                Assert.Empty(handler.RequestedUris);
                _httpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
            }

            AssertAudited(expectedStatus);
        }

        [Theory]
        [InlineData(Authority + "/.well-known/openid-configuration")]
        [InlineData(TokenEndpoint)]
        public async Task GivenResponseFromTestHandler_WhenClientIsDisposed_ThenResponseContentIsDisposed(string requestUri)
        {
            using var handler = new DiscoveryHttpMessageHandler(failDiscovery: false);
            using var client = new HttpClient(handler);
            using var response = await client.GetAsync(new Uri(requestUri));
            Assert.NotEmpty(await response.Content.ReadAsStringAsync());

            client.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => response.Content.ReadAsStringAsync());
        }

        private IHost CreateHost(bool enableProxy, string authority, DiscoveryHttpMessageHandler handler)
        {
            // Capture the status during auditing, before TestServer disposes the request context.
            _auditHelper.When(helper => helper.LogExecuted(
                Arg.Any<HttpContext>(),
                Arg.Any<AadSmartOnFhirClaimsExtractor>(),
                Arg.Any<bool>(),
                Arg.Any<long>()))
                .Do(call => _auditedStatusCode = call.Arg<HttpContext>().Response.StatusCode);

            var configuration = new SecurityConfiguration
            {
                EnableAadSmartOnFhirProxy = enableProxy,
                Authentication = new AuthenticationConfiguration { Authority = authority },
            };
            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveRouteNameUrl(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>())
                .Returns(new Uri("https://fhir.example/AadSmartOnFhirProxy/callback/encoded"));

            return new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(Options.Create(configuration));
                    services.AddSingleton(_ => new HttpClient(handler, disposeHandler: false));
                    services.AddSingleton(provider =>
                    {
                        _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(provider.GetRequiredService<HttpClient>());
                        return _httpClientFactory;
                    });
                    services.AddSingleton(_auditHelper);
                    services.AddSingleton(urlResolver);
                    services.AddHttpContextAccessor();
                    services.AddSingleton<AadSmartOnFhirClaimsExtractor>();
                    services.AddSingleton<AadSmartOnFhirProxyAuditLoggingFilterAttribute>();
                    services.AddControllers().ConfigureApplicationPartManager(manager =>
                    {
                        manager.ApplicationParts.Clear();
                        manager.FeatureProviders.Add(new ProxyControllerFeatureProvider());
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).Start();
        }

        private void AssertAudited(HttpStatusCode statusCode)
        {
            _auditHelper.Received(1).LogExecuting(Arg.Any<HttpContext>(), Arg.Any<AadSmartOnFhirClaimsExtractor>());
            _auditHelper.Received(1).LogExecuted(
                Arg.Any<HttpContext>(),
                Arg.Any<AadSmartOnFhirClaimsExtractor>(),
                Arg.Any<bool>(),
                Arg.Any<long>());
            Assert.Equal((int)statusCode, _auditedStatusCode);
        }

        private static HttpRequestMessage CreateRequest(string action, string grantType = "client_credentials")
        {
            if (action == "token")
            {
                var fields = new Dictionary<string, string>
                {
                    { "grant_type", grantType },
                    { "client_id", "test-client" },
                };
                if (grantType == "authorization_code")
                {
                    fields.Add("code", Base64UrlEncoder.Encode("{\"code\":\"test-code\",\"patient\":\"test-patient\"}"));
                    fields.Add("redirect_uri", RedirectUri);
                }

                return new HttpRequestMessage(HttpMethod.Post, "/AadSmartOnFhirProxy/token")
                {
                    Content = new FormUrlEncodedContent(fields),
                };
            }

            var path = action == "authorize"
                ? $"authorize?client_id=test-client&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                : $"callback/{Base64UrlEncoder.Encode(RedirectUri)}?code=test-code&state={Base64UrlEncoder.Encode("{\"l\":\"e30\",\"s\":\"test-state\"}")}";
            return new HttpRequestMessage(HttpMethod.Get, $"/AadSmartOnFhirProxy/{path}");
        }

        private sealed class ProxyControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
        {
            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            {
                feature.Controllers.Add(typeof(AadSmartOnFhirProxyController).GetTypeInfo());
            }
        }

        private sealed class DiscoveryHttpMessageHandler : HttpMessageHandler
        {
            private readonly bool _failDiscovery;
            private readonly HttpResponseMessage _discoveryResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"authorization_endpoint\":\"{AuthorizeEndpoint}\",\"token_endpoint\":\"{TokenEndpoint}\"}}"),
            };

            private readonly HttpResponseMessage _tokenResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"test-token\"}"),
            };

            public DiscoveryHttpMessageHandler(bool failDiscovery)
            {
                _failDiscovery = failDiscovery;
            }

            public List<Uri> RequestedUris { get; } = new List<Uri>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestedUris.Add(request.RequestUri);
                if (request.RequestUri == new Uri($"{Authority}/.well-known/openid-configuration"))
                {
                    if (_failDiscovery)
                    {
                        throw new HttpRequestException("Discovery is unavailable.");
                    }

                    return Task.FromResult(_discoveryResponse);
                }

                Assert.Equal(new Uri(TokenEndpoint), request.RequestUri);
                return Task.FromResult(_tokenResponse);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _discoveryResponse.Dispose();
                    _tokenResponse.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
