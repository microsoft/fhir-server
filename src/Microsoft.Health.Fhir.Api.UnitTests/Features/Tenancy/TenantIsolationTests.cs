// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class TenantIsolationTests : IClassFixture<TenantIsolationTestServerFixture>
    {
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

        private readonly TenantIsolationTestServerFixture _fixture;

        public TenantIsolationTests(TenantIsolationTestServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(TenantIsolationTestServerFixture.AlphaHost, "alpha")]
        [InlineData(TenantIsolationTestServerFixture.BetaHost, "beta")]
        public async Task GivenARequestToATenantHost_WhenAMvcActionResolvesAScopedService_ThenTheTenantInstanceIsUsed(
            string host,
            string expectedTenant)
        {
            using HttpClient client = _fixture.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(new Uri($"https://{host}/mvc/whoami"));

            await AssertTenantMvcResponseAsync(response, expectedTenant);
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
        }

        [Fact]
        public async Task GivenAnUnknownHost_WhenAMvcRequestIsMade_ThenNotFoundIsReturned()
        {
            using HttpClient client = _fixture.CreateClient();
            using HttpResponseMessage response =
                await client.GetAsync(new Uri("https://gamma.example.org/mvc/whoami"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GivenRepeatedMvcRequestsAlternatingTenants_WhenServed_ThenNoTenantBleedOccurs()
        {
            using HttpClient client = _fixture.CreateClient();

            for (int requestNumber = 0; requestNumber < 20; requestNumber++)
            {
                bool isAlpha = requestNumber % 2 == 0;
                string host = isAlpha
                    ? TenantIsolationTestServerFixture.AlphaHost
                    : TenantIsolationTestServerFixture.BetaHost;
                string expectedTenant = isAlpha ? "alpha" : "beta";

                using HttpResponseMessage response =
                    await client.GetAsync(new Uri($"https://{host}/mvc/whoami"));

                await AssertTenantMvcResponseAsync(response, expectedTenant);
            }
        }

        [Fact]
        public void GivenRootCachedMvcOptions_WhenInspected_ThenTenantAffectingInstancesAreFenced()
        {
            MvcOptions options = _fixture.Server.Services
                .GetRequiredService<IOptions<MvcOptions>>()
                .Value;

            string[] filterTypes = options.Filters
                .Select(filter => filter.GetType().FullName)
                .ToArray();

            string[] formatterTypes = options.OutputFormatters
                .Select(formatter => formatter.GetType().FullName)
                .ToArray();

            Assert.Equal(AllowedRootGlobalFilterTypes, filterTypes);
            Assert.Equal(AllowedRootOutputFormatterTypes, formatterTypes);
        }

        private static HttpRequestMessage CreateAuthenticatedRequest(string host, string tenant)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{host}/mvc/secure"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                TenantIsolationTestServerFixture.CreateToken(tenant));

            return request;
        }

        private static async Task AssertTenantMvcResponseAsync(
            HttpResponseMessage response,
            string expectedTenant)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                TenantIsolationTestServerFixture.MvcFilterHeaderValue,
                Assert.Single(response.Headers.GetValues(TenantIsolationTestServerFixture.MvcFilterHeaderName)));

            using JsonDocument content = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expectedTenant, content.RootElement.GetProperty("tenant").GetString());
        }
    }
}
