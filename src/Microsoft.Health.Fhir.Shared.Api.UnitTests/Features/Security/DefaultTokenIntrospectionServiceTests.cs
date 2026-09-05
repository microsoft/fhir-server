// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class DefaultTokenIntrospectionServiceTests
    {
        private const string ExpectedClientId = "ef0c25fd-8da1-47d5-9c85-7ece2c7c1779";
        private const string UserSubject = "9di8U8daZj2Gfq8XmjWGYC3qBanXsnRG8eS7tvH3lcM";

        [Theory]
        [InlineData("appid")]
        [InlineData("azp")]
        public async Task GivenEntraClientIdClaim_WhenIntrospecting_ThenReturnsApplicationIdAsClientId(string clientIdClaimName)
        {
            // Arrange
            var service = CreateService(
                new Claim("sub", UserSubject),
                new Claim(clientIdClaimName, ExpectedClientId));

            // Act
            var response = await service.IntrospectTokenAsync("test-token");

            // Assert
            Assert.Equal(ExpectedClientId, response["client_id"]);
            Assert.Equal(UserSubject, response["sub"]);
        }

        [Fact]
        public async Task GivenExplicitClientIdAndEntraClaims_WhenIntrospecting_ThenPrefersExplicitClientId()
        {
            // Arrange
            const string explicitClientId = "explicit-client-id";
            var service = CreateService(
                new Claim("sub", UserSubject),
                new Claim("appid", ExpectedClientId),
                new Claim("azp", ExpectedClientId),
                new Claim("client_id", explicitClientId));

            // Act
            var response = await service.IntrospectTokenAsync("test-token");

            // Assert
            Assert.Equal(explicitClientId, response["client_id"]);
        }

        [Fact]
        public async Task GivenOnlySubjectClaim_WhenIntrospecting_ThenUsesSubjectAsClientId()
        {
            // Arrange
            var service = CreateService(new Claim("sub", ExpectedClientId));

            // Act
            var response = await service.IntrospectTokenAsync("test-token");

            // Assert
            Assert.Equal(ExpectedClientId, response["client_id"]);
        }

        private static StubTokenIntrospectionService CreateService(params Claim[] claims)
        {
            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory
                .CreateClient(DefaultTokenIntrospectionService.OidcConfigurationHttpClientName)
                .Returns(new HttpClient());

            var securityConfiguration = new SecurityConfiguration
            {
                Authentication = new AuthenticationConfiguration
                {
                    Authority = "https://issuer.example.com",
                    Audience = "https://fhir.example.com",
                },
            };

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return new StubTokenIntrospectionService(
                Options.Create(securityConfiguration),
                httpClientFactory,
                principal);
        }

        private sealed class StubTokenIntrospectionService : DefaultTokenIntrospectionService
        {
            private readonly ClaimsPrincipal _principal;

            public StubTokenIntrospectionService(
                IOptions<SecurityConfiguration> securityConfiguration,
                IHttpClientFactory httpClientFactory,
                ClaimsPrincipal principal)
                : base(
                    securityConfiguration,
                    NullLogger<DefaultTokenIntrospectionService>.Instance,
                    httpClientFactory)
            {
                _principal = principal;
            }

            protected override Task<TokenValidationResult> ValidateTokenAsync(
                string token,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(TokenValidationResult.Valid(new JwtSecurityToken(), _principal));
            }
        }
    }
}
