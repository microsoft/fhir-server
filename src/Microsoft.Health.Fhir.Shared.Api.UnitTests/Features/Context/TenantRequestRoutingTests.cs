// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class TenantRequestRoutingTests
    {
        public static IEnumerable<object[]> ServiceAuthenticationRequests()
        {
            foreach (int tenantCount in new[] { 0, 1, 2 })
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
                    yield return new object[] { tenantCount, request.Path, request.Method };
                }
            }
        }

        [Theory]
        [InlineData(0, "/Patient", "http://localhost/", null)]
        [InlineData(1, "/Patient", "http://localhost/", "tenant-a")]
        [InlineData(1, "/tenant-a/Patient", "http://localhost/tenant-a/", "tenant-a")]
        [InlineData(2, "/tenant-a/Patient", "http://localhost/tenant-a/", "tenant-a")]
        [InlineData(2, "/tenant-b/Patient", "http://localhost/tenant-b/", "tenant-b")]
        [InlineData(2, "/tenant-a/Patient?name=Alice", "http://localhost/tenant-a/", "tenant-a")]
        public async Task GivenConfiguredTenants_WhenRequestingPatients_ThenRouteAndBaseUriAreTenantAware(int tenantCount, string path, string baseUri, string tenantId)
        {
            using var host = CreateHost(tenantCount);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(baseUri, await response.Content.ReadAsStringAsync());
            Assert.Equal(baseUri + "Patient", Assert.Single(response.Headers.GetValues("X-Test-Link")));
            Assert.Equal(new Uri(client.BaseAddress, path).AbsoluteUri, Assert.Single(response.Headers.GetValues("X-Test-Uri")));
            if (tenantId == null)
            {
                Assert.False(response.Headers.Contains("X-Test-Tenant"));
            }
            else
            {
                Assert.Equal(tenantId, Assert.Single(response.Headers.GetValues("X-Test-Tenant")));
            }
        }

        [Theory]
        [InlineData("/Patient")]
        [InlineData("/unknown/Patient")]
        [InlineData("/tenant-aa/Patient")]
        [InlineData("/TENANT-A/Patient")]
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
        public async Task GivenMultipleTenants_WhenTenantIsMissingOrUnknown_ThenRequestIsRejected(string path)
        {
            using var host = CreateHost(2);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task GivenConfiguredTenants_WhenRequestingHealthCheckWithoutTenant_ThenHealthCheckIsAccessible(int tenantCount)
        {
            using var host = CreateHost(tenantCount);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri("/health/check", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("healthy", await response.Content.ReadAsStringAsync());
            if (tenantCount == 1)
            {
                Assert.Equal("tenant-a", Assert.Single(response.Headers.GetValues("X-Test-Tenant")));
            }
            else
            {
                Assert.False(response.Headers.Contains("X-Test-Tenant"));
            }
        }

        [Theory]
        [MemberData(nameof(ServiceAuthenticationRequests))]
        public async Task GivenConfiguredTenants_WhenRequestingServiceAuthentication_ThenNoTenantIsSelected(int tenantCount, string path, string method)
        {
            using var host = CreateHost(tenantCount);
            using var client = host.GetTestClient();
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Test-Tenant"));
            Assert.Equal(new Uri(client.BaseAddress, path).AbsoluteUri, Assert.Single(response.Headers.GetValues("X-Test-Uri")));
        }

        [Theory]
        [InlineData("connect", "/connect/token")]
        [InlineData(".well-known", "/.well-known/openid-configuration")]
        [InlineData("AadSmartOnFhirProxy", "/AadSmartOnFhirProxy/callback/redirect")]
        public async Task GivenTenantIdMatchingAuthenticationPrefix_WhenRequestingServiceAuthentication_ThenPathIsNotTreatedAsTenantPrefix(string tenantId, string path)
        {
            using var host = CreateHost(2, tenantId);
            using var client = host.GetTestClient();
            using var request = new HttpRequestMessage(path == "/connect/token" ? HttpMethod.Post : HttpMethod.Get, new Uri(path, UriKind.Relative));

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Test-Tenant"));
        }

        [Fact]
        public async Task GivenExistingPathBase_WhenRequestingServiceAuthentication_ThenServiceBaseUriIsPreserved()
        {
            using var host = CreateHost(2, pathBase: "/fhir");
            using var client = host.GetTestClient();

            using var response = await client.PostAsync(new Uri("/fhir/connect/token", UriKind.Relative), content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/fhir/", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Test-Tenant"));
            Assert.Equal("http://localhost/fhir/connect/token", Assert.Single(response.Headers.GetValues("X-Test-Uri")));
        }

        private static IHost CreateHost(int tenantCount, string firstTenantId = "tenant-a", string pathBase = null)
        {
            var values = new Dictionary<string, string>();
            for (int i = 0; i < tenantCount; i++)
            {
                values[$"FhirServer:TenantConfiguration:Tenants:{i}:Id"] = i == 0 ? firstTenantId : "tenant-b";
                values[$"FhirServer:TenantConfiguration:Tenants:{i}:DatabaseName"] = $"database-{i}";
            }

            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            return new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddFhirServer(configuration);
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
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/Patient", async context =>
                        {
                            var accessor = context.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                            if (accessor.RequestContext.TenantId != null)
                            {
                                context.Response.Headers["X-Test-Tenant"] = accessor.RequestContext.TenantId;
                            }

                            var linkGenerator = context.RequestServices.GetRequiredService<LinkGenerator>();
                            context.Response.Headers["X-Test-Link"] = linkGenerator.GetUriByName(context, "patients", values: null);
                            context.Response.Headers["X-Test-Uri"] = accessor.RequestContext.Uri.AbsoluteUri;
                            await context.Response.WriteAsync(accessor.RequestContext.BaseUri.AbsoluteUri);
                        }).WithName("patients");
                        endpoints.MapGet("/health/check", context =>
                        {
                            var accessor = context.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                            if (accessor.RequestContext.TenantId != null)
                            {
                                context.Response.Headers["X-Test-Tenant"] = accessor.RequestContext.TenantId;
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
                                if (accessor.RequestContext.TenantId != null)
                                {
                                    context.Response.Headers["X-Test-Tenant"] = accessor.RequestContext.TenantId;
                                }

                                context.Response.Headers["X-Test-Uri"] = accessor.RequestContext.Uri.AbsoluteUri;
                                return context.Response.WriteAsync(accessor.RequestContext.BaseUri.AbsoluteUri);
                            });
                        }
                    });
                    app.Run(context => context.Response.WriteAsync("unmatched route"));
                })).Start();
        }
    }
}
