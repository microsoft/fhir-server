// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class TenantIsolationTests : IClassFixture<TenantIsolationTestServerFixture>
    {
        private static readonly string[] AllowedRootCacheProfileNames = Array.Empty<string>();

        private static readonly string[] AllowedRootConventionTypes = Array.Empty<string>();

        private static readonly string[] AllowedRootOutputFormatterTypes =
        {
            "Microsoft.AspNetCore.Mvc.Formatters.HttpNoContentOutputFormatter",
            "Microsoft.AspNetCore.Mvc.Formatters.StringOutputFormatter",
            "Microsoft.AspNetCore.Mvc.Formatters.StreamOutputFormatter",
            "Microsoft.AspNetCore.Mvc.Formatters.SystemTextJsonOutputFormatter",
        };

        private static readonly string[] AllowedRootGlobalFilterTypes =
        {
            "Microsoft.AspNetCore.Mvc.ModelBinding.UnsupportedContentTypeFilter",
            typeof(TenantIsolationTestServerFixture.TenantNeutralGlobalFilter).FullName,
        };

        private static readonly string[] AllowedRootInputFormatterTypes =
        {
            "Microsoft.AspNetCore.Mvc.Formatters.SystemTextJsonInputFormatter",
        };

        private static readonly string[] AllowedRootModelBinderProviderTypes =
        {
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.BinderTypeModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.ServicesModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.BodyModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.HeaderModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.FloatingPointTypeModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.EnumTypeModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.DateTimeModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.SimpleTypeModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.TryParseModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.CancellationTokenModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.ByteArrayModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.FormFileModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.FormCollectionModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.KeyValuePairModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.DictionaryModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.ArrayModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.CollectionModelBinderProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Binders.ComplexObjectModelBinderProvider",
        };

        private static readonly string[] AllowedRootModelMetadataDetailsProviderTypes =
        {
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.ExcludeBindingMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.DefaultBindingMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.DefaultValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.BindingSourceMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.BindingSourceMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.BindingSourceMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.BindingSourceMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.BindingSourceMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.SuppressChildValidationMetadataProvider",
            "Microsoft.AspNetCore.Mvc.DataAnnotations.DataAnnotationsMetadataProvider",
            "Microsoft.AspNetCore.Mvc.ModelBinding.Validation.HasValidatorsValidationMetadataProvider",
        };

        private static readonly string[] AllowedRootModelValidatorProviderTypes =
        {
            "Microsoft.AspNetCore.Mvc.ModelBinding.Validation.DefaultModelValidatorProvider",
            "Microsoft.AspNetCore.Mvc.DataAnnotations.DataAnnotationsModelValidatorProvider",
        };

        private static readonly string[] AllowedRootValueProviderFactoryTypes =
        {
            "Microsoft.AspNetCore.Mvc.ModelBinding.FormValueProviderFactory",
            "Microsoft.AspNetCore.Mvc.ModelBinding.RouteValueProviderFactory",
            "Microsoft.AspNetCore.Mvc.ModelBinding.QueryStringValueProviderFactory",
            "Microsoft.AspNetCore.Mvc.ModelBinding.JQueryFormValueProviderFactory",
            "Microsoft.AspNetCore.Mvc.ModelBinding.FormFileValueProviderFactory",
        };

        private readonly TenantIsolationTestServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TenantIsolationTests(
            TenantIsolationTestServerFixture fixture,
            ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Theory]
        [InlineData(TenantIsolationTestServerFixture.AlphaHost, "alpha")]
        [InlineData(TenantIsolationTestServerFixture.BetaHost, "beta")]
        public async Task GivenARequestToATenantHost_WhenMvcActivatesAControllerWithConstructorInjection_ThenTheTenantInstanceIsUsed(
            string host,
            string expectedTenant)
        {
            using HttpClient client = _fixture.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(new Uri($"https://{host}/mvc/constructor"));

            await AssertTenantMvcResponseAsync(response, expectedTenant);
        }

        [Theory]
        [InlineData(TenantIsolationTestServerFixture.AlphaHost, "alpha")]
        [InlineData(TenantIsolationTestServerFixture.BetaHost, "beta")]
        public async Task GivenARequestToATenantHost_WhenMvcBindsRouteAndQueryValuesAndAFromServicesParameter_ThenTheTenantInstanceIsUsed(
            string host,
            string expectedTenant)
        {
            const string RouteValue = "bound-route";
            const string QueryValue = "bound-query";
            using HttpClient client = _fixture.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                new Uri($"https://{host}/mvc/from-services/{RouteValue}?queryValue={QueryValue}"));

            JsonElement body = await AssertTenantMvcResponseAsync(response, expectedTenant);

            Assert.Equal(RouteValue, body.GetProperty("routeValue").GetString());
            Assert.Equal(QueryValue, body.GetProperty("queryValue").GetString());
        }

        [Theory]
        [InlineData(TenantIsolationTestServerFixture.AlphaHost, "alpha")]
        [InlineData(TenantIsolationTestServerFixture.BetaHost, "beta")]
        public async Task GivenARequestToATenantHost_WhenMvcActivatesATypeFilter_ThenTheFilterUsesTheTenantInstance(
            string host,
            string expectedTenant)
        {
            using HttpClient client = _fixture.CreateClient();
            using HttpResponseMessage response =
                await client.GetAsync(new Uri($"https://{host}/mvc/type-filter/bound-route"));

            await AssertTenantMvcResponseAsync(response, expectedTenant);
            Assert.Equal(
                expectedTenant,
                Assert.Single(response.Headers.GetValues(TenantIsolationTestServerFixture.MvcActivatedFilterHeaderName)));
        }

        [Theory]
        [InlineData(TenantIsolationTestServerFixture.AlphaHost, "alpha")]
        [InlineData(TenantIsolationTestServerFixture.BetaHost, "beta")]
        public async Task GivenATokenForATenant_WhenPresentedToThatTenant_ThenItIsAccepted(
            string host,
            string tenant)
        {
            using HttpClient client = _fixture.CreateClient();
            using var request = CreateAuthenticatedRequest(host, tenant);
            using HttpResponseMessage response = await client.SendAsync(request);

            await AssertTenantMvcResponseAsync(response, tenant);
        }

        [Theory]
        [InlineData(TenantIsolationTestServerFixture.BetaHost, "alpha")]
        [InlineData(TenantIsolationTestServerFixture.AlphaHost, "beta")]
        public async Task GivenATokenForOneTenant_WhenPresentedToAnother_ThenItIsRejected(
            string host,
            string tokenTenant)
        {
            using HttpClient client = _fixture.CreateClient();
            using var request = CreateAuthenticatedRequest(host, tokenTenant);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            AuthenticationHeaderValue challenge = Assert.Single(response.Headers.WwwAuthenticate);
            Assert.Equal(JwtBearerDefaults.AuthenticationScheme, challenge.Scheme);
            Assert.NotNull(challenge.Parameter);
            Assert.StartsWith("error=\"invalid_token\"", challenge.Parameter);
        }

        [Fact]
        public async Task GivenAnUnknownHost_WhenAMvcRequestIsMade_ThenTheTenancyProblemDetailIsReturned()
        {
            using HttpClient client = _fixture.CreateClient();
            using HttpResponseMessage response =
                await client.GetAsync(new Uri("https://gamma.example.org/mvc/whoami"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using JsonDocument content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(404, content.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Unknown FHIR endpoint.", content.RootElement.GetProperty("detail").GetString());
        }

        [Fact]
        public async Task GivenRepeatedMvcRequestsAlternatingTenantsAndActivationPaths_WhenServed_ThenNoTenantBleedOccursAndTheCacheIsReused()
        {
            using HttpClient client = _fixture.CreateClient();

            for (int requestNumber = 0; requestNumber < 24; requestNumber++)
            {
                bool isAlpha = requestNumber % 2 == 0;
                string host = isAlpha
                    ? TenantIsolationTestServerFixture.AlphaHost
                    : TenantIsolationTestServerFixture.BetaHost;
                string expectedTenant = isAlpha ? "alpha" : "beta";
                int activationPath = requestNumber % 3;

                string requestPath = activationPath switch
                {
                    0 => "/mvc/constructor",
                    1 => $"/mvc/from-services/route-{requestNumber}?queryValue=query-{requestNumber}",
                    _ => $"/mvc/type-filter/route-{requestNumber}",
                };

                using HttpResponseMessage response =
                    await client.GetAsync(new Uri($"https://{host}{requestPath}"));

                JsonElement body = await AssertTenantMvcResponseAsync(response, expectedTenant);

                if (activationPath == 1)
                {
                    Assert.Equal($"route-{requestNumber}", body.GetProperty("routeValue").GetString());
                    Assert.Equal($"query-{requestNumber}", body.GetProperty("queryValue").GetString());
                }
                else if (activationPath == 2)
                {
                    Assert.Equal(
                        expectedTenant,
                        Assert.Single(
                            response.Headers.GetValues(
                                TenantIsolationTestServerFixture.MvcActivatedFilterHeaderName)));
                }
            }

            TenantContainerCache cache = _fixture.Server.Services.GetRequiredService<TenantContainerCache>();
            Assert.Equal(2, cache.Count);
            Assert.Equal(0, cache.EvictionCount);
            Assert.Equal(0, cache.AdmissionRejectionCount);

            // Each tenant container is constructed once and thereafter reused, so its probe configurator is
            // invoked exactly once. These totals hold regardless of test execution order because the shared
            // cache never evicts the two resident containers.
            Assert.Equal(2, _fixture.ProbeConfiguratorInvocationCount);
            Assert.Equal(1, _fixture.GetProbeConfiguratorInvocationCount("alpha"));
            Assert.Equal(1, _fixture.GetProbeConfiguratorInvocationCount("beta"));
        }

        [Fact]
        public void GivenRootCachedMvcOptions_WhenInspected_ThenAllMutableCollectionsAreFenced()
        {
            MvcOptions options = _fixture.Server.Services
                .GetRequiredService<IOptions<MvcOptions>>()
                .Value;

            Assert.Equal(
                AllowedRootCacheProfileNames,
                options.CacheProfiles.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
            Assert.Equal(AllowedRootConventionTypes, GetTypeNames(options.Conventions));
            Assert.Equal(AllowedRootGlobalFilterTypes, GetTypeNames(options.Filters));
            Assert.Equal(AllowedRootInputFormatterTypes, GetTypeNames(options.InputFormatters));
            Assert.Equal(AllowedRootModelBinderProviderTypes, GetTypeNames(options.ModelBinderProviders));
            Assert.Equal(
                AllowedRootModelMetadataDetailsProviderTypes,
                GetTypeNames(options.ModelMetadataDetailsProviders));
            Assert.Equal(AllowedRootModelValidatorProviderTypes, GetTypeNames(options.ModelValidatorProviders));
            Assert.Equal(AllowedRootOutputFormatterTypes, GetTypeNames(options.OutputFormatters));
            Assert.Equal(AllowedRootValueProviderFactoryTypes, GetTypeNames(options.ValueProviderFactories));
        }

        [Fact]
        public async Task GivenForwardedHeadersNotEnabled_WhenXForwardedHostMatchesTenantButRawHostDoesNot_ThenTenantIsNotResolved()
        {
            using HttpClient client = _fixture.CreateForwardedHeadersDisabledClient();
            using HttpRequestMessage request = CreateForwardedHostRequest(
                "/forwarded-host/tenant",
                TenantIsolationTestServerFixture.AlphaHost);

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using JsonDocument content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(404, content.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Unknown FHIR endpoint.", content.RootElement.GetProperty("detail").GetString());
        }

        [Theory]
        [InlineData(TenantIsolationTestServerFixture.AlphaHost, "alpha")]
        [InlineData(TenantIsolationTestServerFixture.BetaHost, "beta")]
        public async Task GivenForwardedHeadersAndTenancyEnabled_WhenXForwardedHostMatchesTenant_ThenExternalTenantIsResolved(
            string externalHost,
            string expectedTenant)
        {
            using HttpClient client = _fixture.CreateForwardedHeadersEnabledClient();
            using HttpRequestMessage request =
                CreateForwardedHostRequest("/forwarded-host/tenant", externalHost);

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expectedTenant, content.RootElement.GetProperty("tenant").GetString());
        }

        private static HttpRequestMessage CreateForwardedHostRequest(string path, string forwardedHost)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri($"https://{TenantIsolationTestServerFixture.InternalProxyHost}{path}"));
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", forwardedHost);

            return request;
        }

        private static HttpRequestMessage CreateAuthenticatedRequest(string host, string tenant)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{host}/mvc/secure"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                TenantIsolationTestServerFixture.CreateToken(tenant));

            return request;
        }

        private async Task<JsonElement> AssertTenantMvcResponseAsync(
            HttpResponseMessage response,
            string expectedTenant)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                TenantIsolationTestServerFixture.MvcFilterHeaderValue,
                Assert.Single(response.Headers.GetValues(TenantIsolationTestServerFixture.MvcFilterHeaderName)));

            using JsonDocument content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expectedTenant, content.RootElement.GetProperty("tenant").GetString());
            await AssertGlobalFilterMaterializationOriginAsync(response, expectedTenant);

            return content.RootElement.Clone();
        }

        private async Task AssertGlobalFilterMaterializationOriginAsync(
            HttpResponseMessage response,
            string expectedTenant)
        {
            string executingIdentity = Assert.Single(
                response.Headers.GetValues(TenantIsolationTestServerFixture.MvcGlobalFilterIdentityHeaderName));

            MvcGlobalFilterMaterializationOrigin origin;
            if (StringComparer.Ordinal.Equals(executingIdentity, _fixture.RootMvcGlobalFilterIdentity))
            {
                // The executing filter is exactly the instance the root service provider materialized.
                Assert.Equal(_fixture.RootMvcGlobalFilterIdentity, executingIdentity);
                origin = MvcGlobalFilterMaterializationOrigin.Root;
            }
            else
            {
                // Otherwise it must be exactly the instance the expected tenant's own container materializes.
                // Any other identity (including an unknown one) fails this equality.
                string tenantIdentity = await _fixture.GetTenantMvcGlobalFilterIdentityAsync(expectedTenant);
                Assert.NotEqual(_fixture.RootMvcGlobalFilterIdentity, executingIdentity);
                Assert.Equal(tenantIdentity, executingIdentity);
                origin = MvcGlobalFilterMaterializationOrigin.Tenant;
            }

            _output.WriteLine(
                $"MVC global filter identity: {executingIdentity}; root identity: {_fixture.RootMvcGlobalFilterIdentity}; origin: {origin}.");
        }

        private static string[] GetTypeNames<T>(IEnumerable<T> instances)
        {
            return instances
                .Select(instance => instance.GetType().FullName ?? instance.GetType().Name)
                .ToArray();
        }
    }
}
