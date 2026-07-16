// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class TestFhirServerAuthenticationTests
    {
        private static readonly Uri MetadataTokenEndpoint = new Uri("https://smart-idp.example/token");
        private static readonly Uri MetadataAuthorizeEndpoint = new Uri("https://smart-idp.example/authorize");
        private static readonly Uri OverrideTokenEndpoint = new Uri("https://login.example/tenant/oauth2/token");

        [Fact]
        public async Task GivenMetadataTokenEndpoint_WhenCreatingClientCredentialHandler_ThenMetadataTokenEndpointIsUsed()
        {
            var server = new AuthenticationTestFhirServer();

            await server.ConfigureSecurityOptions();

            TestFhirClient client = server.GetTestFhirClient(ResourceFormat.Json, reusable: false);
            using HttpResponseMessage response = await client.HttpClient.GetAsync("Patient");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(MetadataTokenEndpoint, server.TokenUri);
            Assert.Equal(MetadataTokenEndpoint, server.TokenRequestUri);
        }

        [Fact]
        public async Task GivenConfiguredClientCredentialTokenEndpoint_WhenCreatingClientCredentialHandler_ThenConfiguredEndpointIsUsedAndAuthorizeEndpointRemainsMetadataDerived()
        {
            var server = new ConfiguredAuthenticationTestFhirServer(OverrideTokenEndpoint);

            await server.ConfigureSecurityOptions();

            TestFhirClient client = server.GetTestFhirClient(ResourceFormat.Json, reusable: false);
            using HttpResponseMessage response = await client.HttpClient.GetAsync("Patient");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(MetadataTokenEndpoint, server.TokenUri);
            Assert.Equal(MetadataAuthorizeEndpoint, server.AuthorizeUri);
            Assert.Equal(OverrideTokenEndpoint, server.TokenRequestUri);
        }

        [Fact]
        public void GivenMissingTestTokenEndpoint_WhenParsingSetting_ThenActionableExceptionIsThrown()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => AuthenticationSettings.ParseTestTokenEndpoint(null));

            Assert.Contains("TestTokenEndpoint", exception.Message);
            Assert.Contains("remote E2E tests", exception.Message);
        }

        [Fact]
        public void GivenInvalidTestTokenEndpoint_WhenParsingSetting_ThenActionableExceptionIsThrown()
        {
            const string invalidTokenEndpoint = "not a URI";

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => AuthenticationSettings.ParseTestTokenEndpoint(invalidTokenEndpoint));

            Assert.Contains("TestTokenEndpoint", exception.Message);
            Assert.Contains(invalidTokenEndpoint, exception.Message);
        }

        private class AuthenticationTestFhirServer : TestFhirServer
        {
            public AuthenticationTestFhirServer()
                : base(new Uri("https://fhir.example/"))
            {
            }

            public Uri TokenRequestUri { get; private set; }

            internal override HttpMessageHandler CreateMessageHandler() => new AuthenticationTestMessageHandler(this);

            private sealed class AuthenticationTestMessageHandler : HttpMessageHandler
            {
                private readonly AuthenticationTestFhirServer _server;

                public AuthenticationTestMessageHandler(AuthenticationTestFhirServer server)
                {
                    _server = server;
                }

                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    if (request.RequestUri.AbsolutePath == "/$versions")
                    {
                        return Task.FromResult(CreateResponse(HttpStatusCode.OK, "[]"));
                    }

                    if (request.RequestUri.AbsolutePath == "/metadata")
                    {
                        return Task.FromResult(CreateResponse(HttpStatusCode.OK, CreateMetadata()));
                    }

                    if (request.RequestUri == MetadataTokenEndpoint || request.RequestUri == OverrideTokenEndpoint)
                    {
                        _server.TokenRequestUri = request.RequestUri;
                        return Task.FromResult(CreateResponse(HttpStatusCode.OK, """{"access_token":"eyJhbGciOiJub25lIn0.eyJleHAiOjQxMDI0NDQ4MDB9.c2ln","token_type":"Bearer","expires_in":3600}"""));
                    }

                    if (request.RequestUri.AbsolutePath == "/Patient")
                    {
                        return Task.FromResult(CreateResponse(HttpStatusCode.OK, "{}"));
                    }

                    return Task.FromResult(CreateResponse(HttpStatusCode.NotFound, string.Empty));
                }

                private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string content)
                {
                    return new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(content, Encoding.UTF8, "application/fhir+json"),
                    };
                }

                private static string CreateMetadata()
                {
                    return $$"""
                        {
                          "resourceType": "CapabilityStatement",
                          "rest": [
                            {
                              "mode": "server",
                              "security": {
                                "extension": [
                                  {
                                    "url": "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris",
                                    "extension": [
                                      { "url": "token", "valueUri": "{{MetadataTokenEndpoint}}" },
                                      { "url": "authorize", "valueUri": "{{MetadataAuthorizeEndpoint}}" }
                                    ]
                                  }
                                ]
                              }
                            }
                          ]
                        }
                        """;
                }
            }
        }

        private sealed class ConfiguredAuthenticationTestFhirServer : AuthenticationTestFhirServer
        {
            private readonly Uri _clientCredentialTokenEndpoint;

            public ConfiguredAuthenticationTestFhirServer(Uri clientCredentialTokenEndpoint)
            {
                _clientCredentialTokenEndpoint = clientCredentialTokenEndpoint;
            }

            protected override Uri GetClientCredentialTokenEndpoint() => _clientCredentialTokenEndpoint;
        }
    }
}
