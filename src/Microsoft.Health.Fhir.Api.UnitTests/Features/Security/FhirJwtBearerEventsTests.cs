// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Logging;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    public class FhirJwtBearerEventsTests
    {
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IClaimsIndexer _claimsIndexer = Substitute.For<IClaimsIndexer>();
        private readonly AspNetCore.Http.HttpContext _httpContext = Substitute.For<AspNetCore.Http.HttpContext>();
        private readonly FhirJwtBearerEvents _fhirJwtBearerEvents;

        public FhirJwtBearerEventsTests()
        {
            _fhirJwtBearerEvents = new FhirJwtBearerEvents(_auditLogger, _fhirRequestContextAccessor, _claimsIndexer);
        }

        [Fact]
        public void GivenAnAuthenticationFailure_WhenJwtBearerEventIsRaised_ThenAnAuditIsLogged()
        {
            var fhirRequestContext = new FhirRequestContext(
                "GET",
                "https://localhost/Patient",
                "https://localhost",
                ValueSets.AuditEventType.RestFulOperation,
                "correlationId",
                requestHeaders: new HeaderDictionary(),
                responseHeaders: new HeaderDictionary());
            _fhirRequestContextAccessor.FhirRequestContext.Returns(fhirRequestContext);

            var claims = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("key", "value"),
            };
            _claimsIndexer.Extract().Returns(claims);

            _fhirJwtBearerEvents.AuthenticationFailed(
                new AspNetCore.Authentication.JwtBearer.AuthenticationFailedContext(
                    _httpContext,
                    new AuthenticationScheme("name", "displayName", typeof(AuthenticationHandler<AuthenticationSchemeOptions>)),
                    new AspNetCore.Authentication.JwtBearer.JwtBearerOptions()));

            _auditLogger.Received(1).LogAudit(
                AuditAction.Executed,
                action: fhirRequestContext.Method,
                resourceType: null,
                requestUri: fhirRequestContext.Uri,
                statusCode: System.Net.HttpStatusCode.Unauthorized,
                correlationId: fhirRequestContext.CorrelationId,
                claims: claims);
        }
    }
}
