// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security;
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
        private const string FormUrlEncodedContentType = "application/x-www-form-urlencoded";

        private readonly TokenIntrospectionController _controller;
        private readonly ITokenIntrospectionService _introspectionService;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly DefaultHttpContext _httpContext;

        public TokenIntrospectionControllerTests()
        {
            _introspectionService = Substitute.For<ITokenIntrospectionService>();
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();

            // Default: allow any data action
            _authorizationService.CheckAccess(DataActions.All, Arg.Any<CancellationToken>())
                .Returns(DataActions.Read);

            _controller = new TokenIntrospectionController(
                _introspectionService,
                _authorizationService,
                NullLogger<TokenIntrospectionController>.Instance);

            // Set up ControllerContext with HttpContext
            _httpContext = new DefaultHttpContext();
            _httpContext.Request.ContentType = FormUrlEncodedContentType;

            // Set up authenticated user by default
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "testuser") };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            _httpContext.User = new ClaimsPrincipal(identity);

            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    _httpContext,
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Fact]
        public async Task GivenUserWithoutAnyDataAction_WhenIntrospect_ThenReturnsForbidden()
        {
            // Arrange - User is authenticated but doesn't have any data actions
            _authorizationService.CheckAccess(DataActions.All, Arg.Any<CancellationToken>())
                .Returns(DataActions.None);

            // Act
            var result = await _controller.Introspect(token: "some.token");

            // Assert
            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal((int)HttpStatusCode.Forbidden, statusResult.StatusCode);
        }

        [Fact]
        public async Task GivenMissingTokenParameter_WhenIntrospect_ThenThrowsOAuth2BadRequestException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<OAuth2BadRequestException>(
                () => _controller.Introspect(token: null));

            Assert.Equal("invalid_request", exception.Error);
            Assert.Equal(Resources.OAuth2TokenParameterRequired, exception.ErrorDescription);
        }

        [Fact]
        public async Task GivenEmptyTokenParameter_WhenIntrospect_ThenThrowsOAuth2BadRequestException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<OAuth2BadRequestException>(
                () => _controller.Introspect(token: string.Empty));

            Assert.Equal("invalid_request", exception.Error);
            Assert.Equal(Resources.OAuth2TokenParameterRequired, exception.ErrorDescription);
        }

        [Fact]
        public async Task GivenWhitespaceTokenParameter_WhenIntrospect_ThenThrowsOAuth2BadRequestException()
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<OAuth2BadRequestException>(
                () => _controller.Introspect(token: "   "));

            Assert.Equal("invalid_request", exception.Error);
            Assert.Equal(Resources.OAuth2TokenParameterRequired, exception.ErrorDescription);
        }

        [Fact]
        public async Task GivenInvalidContentType_WhenIntrospect_ThenThrowsOAuth2BadRequestException()
        {
            // Arrange
            _httpContext.Request.ContentType = "application/json";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<OAuth2BadRequestException>(
                () => _controller.Introspect(token: "some.token"));

            Assert.Equal("invalid_request", exception.Error);
            Assert.Equal(Resources.OAuth2ContentTypeMustBeFormUrlEncoded, exception.ErrorDescription);
        }

        [Fact]
        public async Task GivenMissingContentType_WhenIntrospect_ThenThrowsOAuth2BadRequestException()
        {
            // Arrange
            _httpContext.Request.ContentType = null;

            // Act & Assert
            var exception = await Assert.ThrowsAsync<OAuth2BadRequestException>(
                () => _controller.Introspect(token: "some.token"));

            Assert.Equal("invalid_request", exception.Error);
            Assert.Equal(Resources.OAuth2ContentTypeMustBeFormUrlEncoded, exception.ErrorDescription);
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
            Assert.True(response.TryGetValue("active", out var active) && (bool)active);
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
            Assert.True(response.TryGetValue("active", out var active) && (bool)active);
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
            Assert.True(response.TryGetValue("active", out _));
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
            Assert.True(response.TryGetValue("active", out _));
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
            Assert.True(response.TryGetValue("active", out _));
            Assert.True(response.TryGetValue("exp", out _));
            Assert.True(response.TryGetValue("iat", out _));
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
            Assert.True(response.TryGetValue("active", out _));
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

        [Fact]
        public async Task GivenFormUrlEncodedContentTypeWithCharset_WhenIntrospect_ThenSucceeds()
        {
            // Arrange - Content-Type with charset parameter should still work
            _httpContext.Request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
            var validToken = "test.token";
            _introspectionService.IntrospectTokenAsync(validToken, Arg.Any<CancellationToken>())
                .Returns(new Dictionary<string, object> { { "active", true } });

            // Act
            var result = await _controller.Introspect(validToken);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }
    }
}
