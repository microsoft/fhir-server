// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class PartitionRequestRoutingTests
    {
        private const string AuthenticationScheme = "PartitionTest";

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task GivenSecurityEnabled_WhenCredentialsAreMissingOrInvalid_ThenPartitionIdsCannotBeDistinguished(int partitionCount)
        {
            using var host = CreateHost(partitionCount, securityEnabled: true);
            using var client = host.GetTestClient();

            foreach (string path in new[] { "/partition/partition-a/Patient", "/partition/partition-b/Patient", "/partition/unknown/Patient", "/partition/PARTITION-A/Patient", "/Patient" })
            {
                foreach (string credentials in new[] { null, "Bearer invalid" })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
                    if (credentials != null)
                    {
                        request.Headers.Add("Authorization", credentials);
                    }

                    using var response = await client.SendAsync(request);

                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                }
            }
        }

        [Theory]
        [InlineData(0, "/Patient", HttpStatusCode.OK, null)]
        [InlineData(1, "/Patient", HttpStatusCode.OK, "partition-a")]
        [InlineData(2, "/Patient", HttpStatusCode.NotFound, null)]
        [InlineData(2, "/partition/partition-a/Patient", HttpStatusCode.OK, "partition-a")]
        [InlineData(2, "/partition/partition-b/Patient", HttpStatusCode.OK, "partition-b")]
        [InlineData(2, "/partition/PARTITION-A/Patient", HttpStatusCode.NotFound, null)]
        [InlineData(2, "/partition", HttpStatusCode.NotFound, null)]
        [InlineData(1, "/partition/unknown/Patient", HttpStatusCode.NotFound, null)]
        [InlineData(2, "/partition/unknown/Patient", HttpStatusCode.NotFound, null)]
        public async Task GivenAuthenticatedRequest_WhenResolvingPartition_ThenConfiguredPartitionIsRequired(int partitionCount, string path, HttpStatusCode status, string partitionId)
        {
            using var host = CreateHost(partitionCount, securityEnabled: true);
            using var client = host.GetTestClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            request.Headers.Add("Authorization", "Bearer valid");

            using var response = await client.SendAsync(request);

            Assert.Equal(status, response.StatusCode);
            if (partitionId != null)
            {
                Assert.Equal(partitionId, Assert.Single(response.Headers.GetValues("X-Test-Partition")));
            }
        }

        public static IEnumerable<object[]> ServiceAuthenticationRequests()
        {
            foreach (int partitionCount in new[] { 0, 1, 2 })
            {
                foreach (var request in new[]
                {
                    (Path: "/connect/token", Method: "POST"),
                    (Path: "/CONNECT/TOKEN", Method: "POST"),
                    (Path: "/connect/token/", Method: "POST"),
                    (Path: "/connect/introspect", Method: "POST"),
                    (Path: "/.well-known/openid-configuration", Method: "GET"),
                    (Path: "/.well-known/oauth-authorization-server", Method: "GET"),
                    (Path: "/.well-known/jwks", Method: "GET"),
                    (Path: "/.well-known/smart-configuration", Method: "GET"),
                    (Path: "/AadSmartOnFhirProxy/authorize", Method: "GET"),
                    (Path: "/AadSmartOnFhirProxy/token", Method: "POST"),
                    (Path: "/AadSmartOnFhirProxy/callback/aHR0cHM6Ly9jbGllbnQ", Method: "GET"),
                    (Path: "/AadSmartOnFhirProxy/callback/aHR0cHM6Ly9jbGllbnQ/", Method: "GET"),
                })
                {
                    yield return new object[] { partitionCount, request.Path, request.Method };
                }
            }
        }

        [Theory]
        [InlineData(0, "/Patient", "http://localhost/", null)]
        [InlineData(1, "/Patient", "http://localhost/", "partition-a")]
        [InlineData(1, "/partition/partition-a/Patient", "http://localhost/partition/partition-a/", "partition-a")]
        [InlineData(2, "/partition/partition-a/Patient", "http://localhost/partition/partition-a/", "partition-a")]
        [InlineData(2, "/partition/partition-b/Patient", "http://localhost/partition/partition-b/", "partition-b")]
        [InlineData(2, "/PARTITION/partition-a/Patient", "http://localhost/PARTITION/partition-a/", "partition-a")]
        [InlineData(2, "/partition/partition-a/Patient?name=Alice", "http://localhost/partition/partition-a/", "partition-a")]
        public async Task GivenConfiguredPartitions_WhenRequestingPatients_ThenRouteAndBaseUriArePartitionAware(int partitionCount, string path, string baseUri, string partitionId)
        {
            using var host = CreateHost(partitionCount);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(baseUri, await response.Content.ReadAsStringAsync());
            Assert.Equal(baseUri + "Patient", Assert.Single(response.Headers.GetValues("X-Test-Link")));
            Assert.Equal(new Uri(client.BaseAddress, path).AbsoluteUri, Assert.Single(response.Headers.GetValues("X-Test-Uri")));
            if (partitionId == null)
            {
                Assert.False(response.Headers.Contains("X-Test-Partition"));
            }
            else
            {
                Assert.Equal(partitionId, Assert.Single(response.Headers.GetValues("X-Test-Partition")));
            }
        }

        [Theory]
        [InlineData("/Patient")]
        [InlineData("/unknown/Patient")]
        [InlineData("/partition-aa/Patient")]
        [InlineData("/partition-a/Patient")]
        [InlineData("/PARTITION-A/Patient")]
        [InlineData("/partition/unknown/Patient")]
        [InlineData("/partition/PARTITION-A/Patient")]
        [InlineData("/partition")]
        [InlineData("/partition/")]
        [InlineData("/partition//Patient")]
        [InlineData("/health/check/extra")]
        [InlineData("/health/check/")]
        [InlineData("/connect/token/extra")]
        [InlineData("/connect/introspect/extra")]
        [InlineData("/connect/Patient")]
        [InlineData("/.well-known/openid-configuration/extra")]
        [InlineData("/.well-known/unrelated")]
        [InlineData("/AadSmartOnFhirProxy/Patient")]
        [InlineData("/AadSmartOnFhirProxy/callback")]
        [InlineData("/AadSmartOnFhirProxy/callback/")]
        [InlineData("/AadSmartOnFhirProxy/callback/redirect/extra")]
        [InlineData("/")]
        public async Task GivenMultiplePartitions_WhenPartitionIsMissingOrUnknown_ThenRequestIsRejected(string path)
        {
            using var host = CreateHost(2);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData("/partition")]
        [InlineData("/partition/")]
        [InlineData("/partition/unknown/Patient")]
        [InlineData("/partition/PARTITION-A/Patient")]
        public async Task GivenOnePartition_WhenExplicitPartitionIsMissingOrUnknown_ThenRequestIsRejected(string path)
        {
            using var host = CreateHost(1);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("metadata")]
        [InlineData("_history")]
        [InlineData("connect")]
        [InlineData("partition")]
        public async Task GivenPartitionNamedAfterRoute_WhenRequestingPartition_ThenPartitionIsSelected(string partitionId)
        {
            using var host = CreateHost(2, partitionId);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri($"/partition/{partitionId}/Patient", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal($"http://localhost/partition/{partitionId}/", await response.Content.ReadAsStringAsync());
            Assert.Equal(partitionId, Assert.Single(response.Headers.GetValues("X-Test-Partition")));
        }

        [Fact]
        public async Task GivenExistingPathBase_WhenRequestingPartition_ThenBothPrefixesArePreserved()
        {
            using var host = CreateHost(2, pathBase: "/fhir");
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri("/fhir/partition/partition-a/Patient", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/fhir/partition/partition-a/", await response.Content.ReadAsStringAsync());
            Assert.Equal("http://localhost/fhir/partition/partition-a/Patient", Assert.Single(response.Headers.GetValues("X-Test-Link")));
            Assert.Equal("http://localhost/fhir/partition/partition-a/Patient", Assert.Single(response.Headers.GetValues("X-Test-Uri")));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task GivenConfiguredPartitions_WhenRequestingHealthCheckWithoutPartition_ThenHealthCheckIsAccessible(int partitionCount)
        {
            using var host = CreateHost(partitionCount);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri("/health/check", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("healthy", await response.Content.ReadAsStringAsync());
            if (partitionCount == 1)
            {
                Assert.Equal("partition-a", Assert.Single(response.Headers.GetValues("X-Test-Partition")));
            }
            else
            {
                Assert.False(response.Headers.Contains("X-Test-Partition"));
            }
        }

        [Theory]
        [MemberData(nameof(ServiceAuthenticationRequests))]
        public async Task GivenConfiguredPartitions_WhenRequestingServiceAuthentication_ThenNoPartitionIsSelected(int partitionCount, string path, string method)
        {
            using var host = CreateHost(partitionCount, securityEnabled: true);
            using var client = host.GetTestClient();
            foreach (string credentials in new[] { null, "Bearer valid" })
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
                if (credentials != null)
                {
                    request.Headers.Add("Authorization", credentials);
                }

                using var response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("http://localhost/", await response.Content.ReadAsStringAsync());
                Assert.False(response.Headers.Contains("X-Test-Partition"));
                Assert.Equal(new Uri(client.BaseAddress, path).AbsoluteUri, Assert.Single(response.Headers.GetValues("X-Test-Uri")));
            }
        }

        [Theory]
        [InlineData("connect", "/connect/token")]
        [InlineData(".well-known", "/.well-known/openid-configuration")]
        [InlineData("AadSmartOnFhirProxy", "/AadSmartOnFhirProxy/callback/redirect")]
        public async Task GivenPartitionIdMatchingAuthenticationPrefix_WhenRequestingServiceAuthentication_ThenPathIsNotTreatedAsPartitionPrefix(string partitionId, string path)
        {
            using var host = CreateHost(2, partitionId);
            using var client = host.GetTestClient();
            using var request = new HttpRequestMessage(path == "/connect/token" ? HttpMethod.Post : HttpMethod.Get, new Uri(path, UriKind.Relative));

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Test-Partition"));
        }

        [Fact]
        public async Task GivenExistingPathBase_WhenRequestingServiceAuthentication_ThenServiceBaseUriIsPreserved()
        {
            using var host = CreateHost(2, pathBase: "/fhir");
            using var client = host.GetTestClient();

            using var response = await client.PostAsync(new Uri("/fhir/connect/token", UriKind.Relative), content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/fhir/", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Test-Partition"));
            Assert.Equal("http://localhost/fhir/connect/token", Assert.Single(response.Headers.GetValues("X-Test-Uri")));
        }

        private static IHost CreateHost(int partitionCount, string firstPartitionId = "partition-a", string pathBase = null, bool securityEnabled = false)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < partitionCount; i++)
            {
                ids.Add(i == 0 ? firstPartitionId : "partition-b");
            }

            var manager = Substitute.For<IPartitionManager>();
            manager.Partitions.Returns(ids);
            IConfiguration configuration = new ConfigurationBuilder().Build();
            return new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddAuthentication(AuthenticationScheme)
                        .AddScheme<AuthenticationSchemeOptions, PartitionTestAuthenticationHandler>(AuthenticationScheme, options => { });
                    services.AddAuthorization();
                    services.AddFhirServer(configuration, server =>
                    {
                        server.Security.Enabled = securityEnabled;
                        server.Security.AddAuthenticationLibrary = (_, _) => { };
                    });
                    services.AddSingleton(manager);
                    services.RemoveAll<IStartupFilter>();
                    services.RemoveAll<IHostedService>();
                    services.AddSingleton<CorrelationIdProvider>(() => Guid.NewGuid().ToString());
                })
                .Configure(app =>
                {
                    if (pathBase != null)
                    {
                        app.UsePathBase(pathBase);
                    }

                    app.UseFhirRequestContext();
                    app.UseAuthentication();
                    app.UseMiddleware<FhirRequestContextAfterAuthenticationMiddleware>();
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        var patients = endpoints.MapGet("/Patient", async context =>
                        {
                            var accessor = context.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                            if (accessor.RequestContext.PartitionId != null)
                            {
                                context.Response.Headers["X-Test-Partition"] = accessor.RequestContext.PartitionId;
                            }

                            var linkGenerator = context.RequestServices.GetRequiredService<LinkGenerator>();
                            context.Response.Headers["X-Test-Link"] = linkGenerator.GetUriByName(context, "patients", values: null);
                            context.Response.Headers["X-Test-Uri"] = accessor.RequestContext.Uri.AbsoluteUri;
                            await context.Response.WriteAsync(accessor.RequestContext.BaseUri.AbsoluteUri);
                        }).WithName("patients");
                        if (securityEnabled)
                        {
                            patients.RequireAuthorization();
                        }

                        endpoints.MapGet("/health/check", context =>
                        {
                            var accessor = context.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                            if (accessor.RequestContext.PartitionId != null)
                            {
                                context.Response.Headers["X-Test-Partition"] = accessor.RequestContext.PartitionId;
                            }

                            return context.Response.WriteAsync("healthy");
                        });
                        foreach (string route in new[]
                        {
                            "/connect/token",
                            "/connect/introspect",
                            "/.well-known/openid-configuration",
                            "/.well-known/oauth-authorization-server",
                            "/.well-known/jwks",
                            "/.well-known/smart-configuration",
                            "/AadSmartOnFhirProxy/authorize",
                            "/AadSmartOnFhirProxy/token",
                            "/AadSmartOnFhirProxy/callback/{encodedRedirect}",
                        })
                        {
                            endpoints.MapMethods(route, new[] { "GET", "POST" }, context =>
                            {
                                var accessor = context.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                                if (accessor.RequestContext.PartitionId != null)
                                {
                                    context.Response.Headers["X-Test-Partition"] = accessor.RequestContext.PartitionId;
                                }

                                context.Response.Headers["X-Test-Uri"] = accessor.RequestContext.Uri.AbsoluteUri;
                                return context.Response.WriteAsync(accessor.RequestContext.BaseUri.AbsoluteUri);
                            });
                        }
                    });
                    app.Run(context => context.Response.WriteAsync("unmatched route"));
                })).Start();
        }

        private sealed class PartitionTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public PartitionTestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
                : base(options, logger, encoder)
            {
            }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                string credentials = Request.Headers.Authorization;
                if (string.IsNullOrEmpty(credentials))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }

                if (credentials != "Bearer valid")
                {
                    return Task.FromResult(AuthenticateResult.Fail("Invalid test credentials."));
                }

                var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") }, AuthenticationScheme));
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthenticationScheme)));
            }
        }
    }
}
