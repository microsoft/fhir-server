// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Audit)]
    public class AuditLoggingMiddlewareTests
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IAuditHelper _auditHelper;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IAuditEventTypeMapping _auditEventTypeMapping;
        private readonly IFhirRequestContext _fhirRequestContext;
        
        private readonly AuditLoggingMiddleware _middleware;

        public AuditLoggingMiddlewareTests()
        {
            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _auditHelper = Substitute.For<IAuditHelper>();
            _claimsExtractor = Substitute.For<IClaimsExtractor>();
            _auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();
            _fhirRequestContext = Substitute.For<IFhirRequestContext>();

            _fhirRequestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            _middleware = new AuditLoggingMiddleware(
                next: context => Task.CompletedTask,
                _fhirRequestContextAccessor,
                _auditHelper,
                _claimsExtractor,
                _auditEventTypeMapping);
        }

        [Fact]
        public async Task GivenA405Response_WhenAuditEventTypeIsEmpty_ThenAuditLoggingShouldBeCalled()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient/123";
            httpContext.Request.Method = "PATCH";
            httpContext.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            
            _fhirRequestContext.AuditEventType.Returns((string)null);

            // Act
            await _middleware.InvokeAsync(httpContext);

            // Assert
            _auditHelper.Received(1).LogExecuted(
                httpContext,
                _claimsExtractor,
                shouldCheckForAuthXFailure: false,
                Arg.Any<long?>());
            
            // Verify that FHIR request context was populated
            _fhirRequestContext.Received().ResourceType = "Patient";
            _fhirRequestContext.Received().AuditEventType = "MethodNotAllowed";
        }

        [Fact]
        public async Task GivenA405Response_WhenAuditEventTypeIsAlreadySet_ThenAuditLoggingShouldNotBeCalled()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient/123";
            httpContext.Request.Method = "PATCH";
            httpContext.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            
            _fhirRequestContext.AuditEventType.Returns("read"); // Already has audit event type

            // Act
            await _middleware.InvokeAsync(httpContext);

            // Assert
            _auditHelper.DidNotReceive().LogExecuted(
                Arg.Any<HttpContext>(),
                Arg.Any<IClaimsExtractor>(),
                Arg.Any<bool>(),
                Arg.Any<long?>());
        }

        [Fact]
        public async Task GivenANon405Response_WhenCalled_ThenAuditLoggingShouldNotBeCalled()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Patient/123";
            httpContext.Request.Method = "GET";
            httpContext.Response.StatusCode = (int)HttpStatusCode.OK;
            
            _fhirRequestContext.AuditEventType.Returns((string)null);

            // Act
            await _middleware.InvokeAsync(httpContext);

            // Assert
            _auditHelper.DidNotReceive().LogExecuted(
                Arg.Any<HttpContext>(),
                Arg.Any<IClaimsExtractor>(),
                Arg.Any<bool>(),
                Arg.Any<long?>());
        }

        [Theory]
        [InlineData("/Patient/123", "Patient")]
        [InlineData("/Observation", "Observation")]
        [InlineData("/DiagnosticReport/abc", "DiagnosticReport")]
        [InlineData("/lowercase/123", null)] // lowercase should not be recognized
        [InlineData("/", null)] // root path
        [InlineData("", null)] // empty path
        public async Task GivenVariousPaths_WhenExtractingResourceType_ThenCorrectResourceTypeIsSet(string path, string expectedResourceType)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;
            httpContext.Request.Method = "PATCH";
            httpContext.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            
            _fhirRequestContext.AuditEventType.Returns((string)null);

            // Act
            await _middleware.InvokeAsync(httpContext);

            // Assert
            if (expectedResourceType != null)
            {
                _fhirRequestContext.Received().ResourceType = expectedResourceType;
            }
            else
            {
                _fhirRequestContext.DidNotReceive().ResourceType = Arg.Any<string>();
            }
        }
    }
}