// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Security;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class AuditMiddlewareTests
    {
        private readonly IClaimsExtractor _claimsExtractor = Substitute.For<IClaimsExtractor>();
        private readonly IAuditHelper _auditHelper = Substitute.For<IAuditHelper>();

        private readonly AuditMiddleware _auditMiddleware;

        private readonly HttpContext _httpContext = new DefaultHttpContext();

        public AuditMiddlewareTests()
        {
            _auditMiddleware = new AuditMiddleware(
                httpContext => Task.CompletedTask,
                _claimsExtractor,
                _auditHelper);
        }

        [Fact]
        public async Task GivenNotAuthXFailure_WhenInvoked_ThenAuditLogShouldNotBeLogged()
        {
            _httpContext.Response.StatusCode = (int)HttpStatusCode.OK;

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.DidNotReceiveWithAnyArgs().LogExecuted(
                httpContext: default,
                claimsExtractor: default);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task GivenAuthXFailed_WhenInvoked_ThenAuditLogShouldBeLogged(HttpStatusCode statusCode)
        {
            _httpContext.Response.StatusCode = (int)statusCode;

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.Received(1).LogExecuted(_httpContext, _claimsExtractor);
        }
    }
}
