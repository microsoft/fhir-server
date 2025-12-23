// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class TokenIntrospectionControllerTests
    {
        private readonly TokenIntrospectionController _controller;
        private readonly ITokenIntrospectionService _introspectionService;

        public TokenIntrospectionControllerTests()
        {
            _introspectionService = Substitute.For<ITokenIntrospectionService>();

            _controller = new TokenIntrospectionController(
                _introspectionService,
                NullLogger<TokenIntrospectionController>.Instance);

            // Set up ControllerContext with HttpContext
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    new DefaultHttpContext(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Fact]
        public async Task GivenMissingTokenParameter_WhenIntrospect_ThenReturnsBadRequest()
        {
            // Act
            var result = await _controller.Introspect(token: null);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequestResult.Value);
        }

        [Fact]
        public async Task GivenEmptyTokenParameter_WhenIntrospect_ThenReturnsBadRequest()
        {
            // Act
            var result = await _controller.Introspect(token: string.Empty);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequestResult.Value);
        }

        [Fact]
        public async Task GivenWhitespaceTokenParameter_WhenIntrospect_ThenReturnsBadRequest()
        {
            // Act
            var result = await _controller.Introspect(token: "   ");

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequestResult.Value);
        }

        [Fact]
        public async Task GivenExpiredToken_WhenIntrospect_ThenReturnsInactive()
        {
            // Arrange
            var expiredToken = "expired.jwt.token";
            _introspectionService.IntrospectTokenAsync(expiredToken, Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, object> { { "active", false } });

            // Act
            var result = await _controller.Introspect(expiredToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.TryGetValue("active", out var active));
            Assert.False((bool)active);
            Assert.Single(response);
        }

        [Fact]
        public async Task GivenMalformedToken_WhenIntrospect_ThenReturnsInactive()
        {
            // Arrange
            var malformedToken = "not.a.valid.jwt.token";
            _introspectionService.IntrospectTokenAsync(malformedToken, Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, object> { { "active", false } });

            // Act
            var result = await _controller.Introspect(malformedToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.TryGetValue("active", out var active));
            Assert.False((bool)active);
            Assert.Single(response);
        }

        [Fact]
        public async Task GivenInvalidSignatureToken_WhenIntrospect_ThenReturnsInactive()
        {
            // Arrange
            var invalidToken = "invalid.signature.token";
            _introspectionService.IntrospectTokenAsync(invalidToken, Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, object> { { "active", false } });

            // Act
            var result = await _controller.Introspect(invalidToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.TryGetValue("active", out var active));
            Assert.False((bool)active);
        }

        [Fact]
        public async Task GivenTokenWithStandardClaims_WhenIntrospect_ThenReturnsActiveWithClaims()
        {
            // Arrange
            var validToken = "valid.jwt.token";
            var expectedResponse = new Dictionary<string, object>
            {
                { "active", true },
                { "sub", "test-user-123" },
                { "client_id", "test-client" },
                { "scope", "patient/Patient.read patient/Observation.read" },
            };
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(expectedResponse);

            // Act
            var result = await _controller.Introspect(validToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.ContainsKey("active"));
            Assert.True((bool)response["active"]);
            Assert.Equal("test-user-123", response["sub"]);
            Assert.Equal("test-client", response["client_id"]);
        }

        [Fact]
        public async Task GivenTokenWithSmartClaims_WhenIntrospect_ThenReturnsActiveWithSmartClaims()
        {
            // Arrange
            var validToken = "valid.smart.token";
            var expectedResponse = new Dictionary<string, object>
            {
                { "active", true },
                { "sub", "test-user-123" },
                { "scope", "patient/Patient.read launch/patient openid fhirUser" },
                { "patient", "Patient/test-patient-456" },
                { "fhirUser", "https://fhir-server.com/Practitioner/test-practitioner-789" },
            };
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(expectedResponse);

            // Act
            var result = await _controller.Introspect(validToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.ContainsKey("active"));
            Assert.True((bool)response["active"]);
            Assert.Equal("Patient/test-patient-456", response["patient"]);
            Assert.Equal("https://fhir-server.com/Practitioner/test-practitioner-789", response["fhirUser"]);
        }

        [Fact]
        public async Task GivenTokenWithRawScope_WhenIntrospect_ThenUsesRawScope()
        {
            // Arrange
            var validToken = "valid.smart.v2.token";
            var rawScope = "patient/Observation.rs?category=vital-signs patient/Patient.read";
            var expectedResponse = new Dictionary<string, object>
            {
                { "active", true },
                { "sub", "test-user" },
                { "scope", rawScope },
            };
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(expectedResponse);

            // Act
            var result = await _controller.Introspect(validToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.ContainsKey("active"));
            Assert.Equal(rawScope, response["scope"]);
        }

        [Fact]
        public async Task GivenTokenWithMultipleScopeClaims_WhenIntrospect_ThenCombinesScopes()
        {
            // Arrange
            var validToken = "valid.multi.scope.token";
            var combinedScopes = "patient/Patient.read patient/Observation.read launch/patient";
            var expectedResponse = new Dictionary<string, object>
            {
                { "active", true },
                { "sub", "test-user" },
                { "scope", combinedScopes },
            };
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(expectedResponse);

            // Act
            var result = await _controller.Introspect(validToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.ContainsKey("active"));
            Assert.Equal(combinedScopes, response["scope"]);
        }

        [Fact]
        public async Task GivenTokenWithExpAndIat_WhenIntrospect_ThenReturnsUnixTimestamps()
        {
            // Arrange
            var validToken = "valid.token.with.timestamps";
            var expectedResponse = new Dictionary<string, object>
            {
                { "active", true },
                { "sub", "test-user" },
                { "exp", 1893456000L }, // Unix timestamp
                { "iat", 1893452400L }, // Unix timestamp
            };
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(expectedResponse);

            // Act
            var result = await _controller.Introspect(validToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.ContainsKey("active"));
            Assert.True(response.ContainsKey("exp"));
            Assert.True(response.ContainsKey("iat"));
        }

        [Fact]
        public async Task GivenTokenWithOnlySubClaim_WhenIntrospect_ThenUsesSubAsClientId()
        {
            // Arrange
            var validToken = "valid.token.sub.only";
            var expectedResponse = new Dictionary<string, object>
            {
                { "active", true },
                { "sub", "test-client-app" },
                { "client_id", "test-client-app" }, // sub used as client_id when not present
            };
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(expectedResponse);

            // Act
            var result = await _controller.Introspect(validToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<Dictionary<string, object>>(okResult.Value);
            Assert.True(response.ContainsKey("active"));
            Assert.Equal("test-client-app", response["sub"]);
            Assert.Equal("test-client-app", response["client_id"]);
        }

        [Fact]
        public async Task GivenValidToken_WhenIntrospect_ThenCallsIntrospectionService()
        {
            // Arrange
            var validToken = "test.token";
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, object> { { "active", true } });

            // Act
            await _controller.Introspect(validToken);

            // Assert
            await _introspectionService.Received(1).IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>());
        }
    }
}
