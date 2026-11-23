// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Partitions;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Core.Features.Routing;
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

        [Fact]
        public async Task GivenPartitionRoute_WhenRequestingPatients_ThenPathsAreUnchangedAndPartitionIsARouteValue()
        {
            using var host = CreateHost(2, pathBase: "/fhir");
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri("/fhir/partition/partition-a/Patient", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("/partition/partition-a/Patient", Assert.Single(response.Headers.GetValues("X-Test-Path")));
            Assert.Equal("/fhir", Assert.Single(response.Headers.GetValues("X-Test-PathBase")));
            Assert.Equal("partition-a", Assert.Single(response.Headers.GetValues("X-Test-Route-Partition")));
        }

        [Fact]
        public async Task GivenProductionControllers_WhenPartitionsAreConfigured_ThenNamedRoutesAndAuthenticationRemainValid()
        {
            using var host = CreateHost(2, securityEnabled: true, productionControllers: true);
            using var client = host.GetTestClient();
            var links = host.Services.GetRequiredService<LinkGenerator>();
            var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

            Assert.Equal("/partition/partition-a/metadata", links.GetPathByName(
                RouteNames.Metadata,
                new RouteValueDictionary { ["partitionId"] = "partition-a" }));
            Assert.DoesNotContain(endpoints, endpoint =>
                endpoint.RoutePattern.GetParameter(PartitionRoute.PartitionId) != null
                && endpoint.Metadata.GetMetadata<PartitionIndependentAttribute>() != null);

            foreach (string id in new[] { "partition-a", "unknown" })
            {
                using var response = await client.PostAsync(new Uri($"/partition/{id}/", UriKind.Relative), content: null);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        [Theory]
        [InlineData(0, "/Patient/_search", "http://localhost/")]
        [InlineData(1, "/partition/partition-a/Patient/_search", "http://localhost/partition/partition-a/")]
        [InlineData(2, "/partition/partition-a/Patient/_search", "http://localhost/partition/partition-a/")]
        public async Task GivenPostSearch_WhenRoutingRequest_ThenSearchTransformationPrecedesPartitionRouting(int partitionCount, string path, string baseUri)
        {
            using var host = CreateHost(partitionCount);
            using var client = host.GetTestClient();
            using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("name", "Alice") });

            using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(baseUri, await response.Content.ReadAsStringAsync());
            Assert.Equal(baseUri + "Patient", Assert.Single(response.Headers.GetValues("X-Test-Link")));
        }

        [Theory]
        [InlineData(1, "/partition/partition-a/metadata", "http://localhost/partition/partition-a/")]
        [InlineData(0, "/metadata", "http://localhost/")]
        public async Task GivenAbsoluteActionRoute_WhenRequestingMetadata_ThenControllerPrefixIsNotDuplicated(int partitionCount, string path, string baseUri)
        {
            using var host = CreateHost(partitionCount);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(baseUri, await response.Content.ReadAsStringAsync());
            Assert.Equal(baseUri + "metadata", Assert.Single(response.Headers.GetValues("X-Test-Metadata-Link")));
            Assert.Equal("http://localhost/.well-known/smart-configuration", Assert.Single(response.Headers.GetValues("X-Test-Service-Link")));
        }

        [Theory]
        [InlineData(0, "/fhir/Patient", "http://localhost/fhir/")]
        [InlineData(1, "/fhir/partition/partition-a/Patient", "http://localhost/fhir/partition/partition-a/")]
        public async Task GivenPartitionRoute_WhenGeneratingResourceAndPagingLinks_ThenLinksRetainTheSelectedBase(int partitionCount, string path, string baseUri)
        {
            using var host = CreateHost(partitionCount, pathBase: "/fhir");
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(baseUri + "Patient/example", Assert.Single(response.Headers.GetValues("X-Test-Resource-Link")));
            Assert.Equal(baseUri + "Patient/example/_history/7", Assert.Single(response.Headers.GetValues("X-Test-Version-Link")));
            Assert.Equal(baseUri + "Patient?ct=next", Assert.Single(response.Headers.GetValues("X-Test-Paging-Link")));
            Assert.Equal("patients", Assert.Single(response.Headers.GetValues("X-Test-Route-Name")));
        }

        [Fact]
        public async Task GivenPartitionRequest_WhenResolvingNamedRoutes_ThenOnlyPartitionRoutesIncludeThePartition()
        {
            using var host = CreateHost(2);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri("/partition/partition-a/Patient", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/partition/partition-a/metadata", Assert.Single(response.Headers.GetValues("X-Test-Metadata-Link")));
            Assert.Equal("http://localhost/.well-known/smart-configuration", Assert.Single(response.Headers.GetValues("X-Test-Service-Link")));
        }

        [Theory]
        [InlineData("/partition/partition-a/")]
        [InlineData("/partition/partition-a")]
        public async Task GivenPartitionRoot_WhenPostingBundle_ThenRootActionIsMatched(string path)
        {
            using var host = CreateHost(2);
            using var client = host.GetTestClient();

            using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("http://localhost/partition/partition-a/", await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task GivenSecurityEnabled_WhenCredentialsAreMissingOrInvalid_ThenPartitionIdsCannotBeDistinguished(int partitionCount)
        {
            using var host = CreateHost(partitionCount, securityEnabled: true);
            using var client = host.GetTestClient();

            foreach (string path in new[] { "/partition/partition-a/Patient", "/partition/partition-b/Patient", "/partition/unknown/Patient", "/partition/PARTITION-A/Patient" })
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
        [InlineData(0, "/partition/partition-a/Patient", HttpStatusCode.NotFound, null)]
        [InlineData(1, "/Patient", HttpStatusCode.NotFound, null)]
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

        [Theory]
        [InlineData("/partition/unknown/Patient")]
        [InlineData("/partition/PARTITION-A/Patient")]
        public async Task GivenAuthenticatedRequest_WhenPartitionIdIsUnrecognized_ThenResponseContainsOperationOutcome(string path)
        {
            using var host = CreateHost(2, securityEnabled: true);
            using var client = host.GetTestClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            request.Headers.Add("Authorization", "Bearer valid");

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/fhir+json", response.Content.Headers.ContentType?.MediaType);
            using var outcome = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("OperationOutcome", outcome.RootElement.GetProperty("resourceType").GetString());
            var issue = Assert.Single(outcome.RootElement.GetProperty("issue").EnumerateArray());
            Assert.Equal("error", issue.GetProperty("severity").GetString());
            Assert.Equal("not-found", issue.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(issue.GetProperty("diagnostics").GetString()));
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
            string expectedLink = path.StartsWith("/PARTITION/", StringComparison.Ordinal)
                ? "http://localhost/partition/partition-a/Patient"
                : baseUri + "Patient";
            Assert.Equal(expectedLink, Assert.Single(response.Headers.GetValues("X-Test-Link")));
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
        [InlineData("/partition/partition-a/Patient", "partition-a")]
        [InlineData("/partition/partition-b/Patient", "partition-b")]
        public async Task GivenPartitionControllerEndpoints_WhenRoutingBundleEntry_ThenPartitionRouteIsMatched(string path, string partitionId)
        {
            using var host = CreateHost(2);
            using var scope = host.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = path;
            var route = new RouteContext(context);

            await scope.ServiceProvider.GetRequiredService<IRouter>().RouteAsync(route);

            Assert.NotNull(route.Handler);
            Assert.Equal(partitionId, route.RouteData.Values["partitionId"]);
            Assert.Equal("PartitionPatients", route.RouteData.Values["controller"]);
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
        [InlineData(0, "/health/check")]
        [InlineData(1, "/health/check")]
        [InlineData(2, "/health/check")]
        [InlineData(0, "/health/check/")]
        [InlineData(1, "/health/check/")]
        [InlineData(2, "/health/check/")]
        public async Task GivenConfiguredPartitions_WhenRequestingHealthCheckWithoutPartition_ThenHealthCheckIsAccessible(int partitionCount, string path)
        {
            using var host = CreateHost(partitionCount);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("healthy", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Test-Partition"));
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

        [Theory]
        [InlineData("/partition/partition-a/connect/token", "POST")]
        [InlineData("/partition/partition-a/connect/introspect", "POST")]
        [InlineData("/partition/partition-a/.well-known/smart-configuration", "GET")]
        public async Task GivenServiceWideEndpoint_WhenUsingPartitionPrefix_ThenServiceOperationIsNotExecuted(string path, string method)
        {
            using var host = CreateHost(2);
            using var client = host.GetTestClient();

            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
            using var response = await client.SendAsync(request);

            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        }

        private static IHost CreateHost(int partitionCount, string firstPartitionId = "partition-a", string pathBase = null, bool securityEnabled = false, bool productionControllers = false)
        {
            var ids = new HashSet<IPartitionInfo>();
            for (int i = 0; i < partitionCount; i++)
            {
                var partition = Substitute.For<IPartitionInfo>();
                partition.Id.Returns(i == 0 ? firstPartitionId : "partition-b");
                ids.Add(partition);
            }

            var manager = Substitute.For<IPartitionManager>();
            manager.IsEnabled.Returns(partitionCount > 0);
            manager.TryGetPartition(Arg.Any<string>(), out Arg.Any<IPartitionInfo>()).Returns(callInfo =>
            {
                var partitionId = callInfo.Arg<string>();
                if (ids.Any(id => id.Id == partitionId))
                {
                    var partition = ids.First(id => id.Id == partitionId);
                    callInfo[1] = partition;
                    return true;
                }

                callInfo[1] = null;
                return false;
            });

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
                    services.AddSingleton(Substitute.For<IAuditEventTypeMapping>());
                    services.AddControllers().ConfigureApplicationPartManager(parts =>
                    {
                        parts.ApplicationParts.Clear();
                        if (productionControllers)
                        {
                            parts.ApplicationParts.Add(new AssemblyPart(typeof(FhirController).Assembly));
                            parts.ApplicationParts.Add(new AssemblyPart(typeof(AadSmartOnFhirProxyController).Assembly));
                        }
                        else
                        {
                            parts.FeatureProviders.Add(new PartitionControllerFeatureProvider());
                        }
                    });
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
                    app.UseFhirRequestContextAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        var patients = endpoints.MapControllers();
                        if (securityEnabled)
                        {
                            patients.RequireAuthorization();
                        }

                        endpoints.MapGet("/health/check", context =>
                        {
                            var accessor = context.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                            if (accessor.RequestContext.Partition != null)
                            {
                                context.Response.Headers["X-Test-Partition"] = accessor.RequestContext.Partition.Id;
                            }

                            return context.Response.WriteAsync("healthy");
                        });
                        foreach (string route in new[]
                        {
                            "/.well-known/openid-configuration",
                            "/.well-known/oauth-authorization-server",
                            "/.well-known/jwks",
                            "/AadSmartOnFhirProxy/authorize",
                            "/AadSmartOnFhirProxy/token",
                            "/AadSmartOnFhirProxy/callback/{encodedRedirect}",
                        })
                        {
                            endpoints.MapMethods(route, new[] { "GET", "POST" }, context =>
                            {
                                var accessor = context.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                                if (accessor.RequestContext.Partition != null)
                                {
                                    context.Response.Headers["X-Test-Partition"] = accessor.RequestContext.Partition.Id;
                                }

                                context.Response.Headers["X-Test-Uri"] = accessor.RequestContext.Uri.AbsoluteUri;
                                return context.Response.WriteAsync(accessor.RequestContext.BaseUri.AbsoluteUri);
                            }).WithMetadata(new PartitionIndependentAttribute());
                        }
                    });
                    app.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return Task.CompletedTask;
                    });
                })).Start();
        }

        private sealed class PartitionControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
        {
            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            {
                feature.Controllers.Add(typeof(PartitionPatientsController).GetTypeInfo());
                feature.Controllers.Add(typeof(PartitionServiceController).GetTypeInfo());
            }
        }

        [PartitionIndependent]
        [AllowAnonymous]
        [Route("connect")]
        public sealed class PartitionServiceController : ControllerBase
        {
            [HttpPost("token")]
            [HttpPost("introspect")]
            public Task Token()
            {
                var context = HttpContext.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>().RequestContext;
                Response.Headers["X-Test-Uri"] = context.Uri.AbsoluteUri;
                if (context.Partition != null)
                {
                    Response.Headers["X-Test-Partition"] = context.Partition.Id;
                }

                return Response.WriteAsync(context.BaseUri.AbsoluteUri);
            }
        }

        [Route("Patient")]
        public sealed class PartitionPatientsController : ControllerBase
        {
            [HttpGet(Name = "patients")]
            public Task Get() => WriteContext();

            [HttpGet("/metadata", Name = RouteNames.Metadata)]
            public Task Metadata() => WriteContext();

            [HttpPost("/", Name = "batch")]
            public Task Batch() => WriteContext();

            [HttpGet("/", Name = "system-search")]
            public Task SystemSearch() => WriteContext();

            [HttpGet("/{typeParameter:regex(^Patient$)}/{idParameter}", Name = RouteNames.ReadResource)]
            public Task Read() => WriteContext();

            [HttpGet("/{typeParameter:regex(^Patient$)}/{idParameter}/_history/{vidParameter}", Name = RouteNames.ReadResourceWithVersionRoute)]
            public Task ReadVersion() => WriteContext();

            [PartitionIndependent]
            [AllowAnonymous]
            [HttpGet("/.well-known/smart-configuration", Name = RouteNames.WellKnownSmartConfiguration)]
            public Task SmartConfiguration() => WriteContext(includeLinks: false);

            private async Task WriteContext(bool includeLinks = true)
            {
                Response.Headers["X-Test-Path"] = Request.Path.Value;
                Response.Headers["X-Test-PathBase"] = Request.PathBase.Value;
                Response.Headers["X-Test-Route-Partition"] = Request.RouteValues["partitionId"]?.ToString();
                var accessor = HttpContext.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>();
                if (accessor.RequestContext.Partition != null)
                {
                    Response.Headers["X-Test-Partition"] = accessor.RequestContext.Partition.Id;
                }

                if (includeLinks)
                {
                    var resolver = HttpContext.RequestServices.GetRequiredService<IUrlResolver>();
                    Response.Headers["X-Test-Link"] = resolver.ResolveRouteNameUrl("patients", new RouteValueDictionary()).AbsoluteUri;
                    Response.Headers["X-Test-Metadata-Link"] = resolver.ResolveMetadataUrl(false).AbsoluteUri;
                    Response.Headers["X-Test-Service-Link"] = resolver.ResolveRouteNameUrl(RouteNames.WellKnownSmartConfiguration, new RouteValueDictionary()).AbsoluteUri;
                    var resource = new Hl7.Fhir.Model.Patient { Id = "example", Meta = new Hl7.Fhir.Model.Meta { VersionId = "7" } }.ToResourceElement();
                    Response.Headers["X-Test-Resource-Link"] = resolver.ResolveResourceUrl(resource).AbsoluteUri;
                    Response.Headers["X-Test-Version-Link"] = resolver.ResolveResourceUrl(resource, includeVersion: true).AbsoluteUri;
                    Response.Headers["X-Test-Paging-Link"] = resolver.ResolveRouteUrl(
                        continuationToken: "next",
                        routeNameOverride: "patients",
                        ambientRouteValuesOverride: new Dictionary<string, object> { ["partitionId"] = "partition-b" }).AbsoluteUri;
                }

                Response.Headers["X-Test-Route-Name"] = accessor.RequestContext.RouteName;
                Response.Headers["X-Test-Uri"] = accessor.RequestContext.Uri.AbsoluteUri;
                await Response.WriteAsync(accessor.RequestContext.BaseUri.AbsoluteUri);
            }
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
