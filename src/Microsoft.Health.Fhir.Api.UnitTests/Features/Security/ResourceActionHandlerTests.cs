// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    public class ResourceActionHandlerTests
    {
        private readonly IAuthorizationPolicy _authorizationPolicy = Substitute.For<IAuthorizationPolicy>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IClaimsIndexer _claimsIndexer = Substitute.For<IClaimsIndexer>();
        private readonly AuthorizationHandlerContext _authorizationHandlerContext = new AuthorizationHandlerContext(new List<IAuthorizationRequirement> { ResourceActionRequirement }, new ClaimsPrincipal(), null);
        private readonly ResourceActionHandler _resourceActionHandler;

        private static readonly ResourceActionRequirement ResourceActionRequirement = new ResourceActionRequirement("Read");

        public ResourceActionHandlerTests()
        {
            _resourceActionHandler = new ResourceActionHandler(_authorizationPolicy, _auditLogger, _fhirRequestContextAccessor, _claimsIndexer);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async void GivenAReadResourceRequest_WhenAuthorizationHandlerHandles_ThenTheAuthorizationHandlerAppropriateStatus(bool authorized)
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

            _authorizationPolicy.HasPermission(Arg.Any<ClaimsPrincipal>(), ResourceAction.Read).ReturnsForAnyArgs(authorized);

            var claims = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("key", "value"),
            };
            _claimsIndexer.Extract().Returns(claims);

            await _resourceActionHandler.HandleAsync(_authorizationHandlerContext);

            Assert.Equal(authorized, _authorizationHandlerContext.HasSucceeded);

            if (!authorized)
            {
                _auditLogger.Received(1).LogAudit(
                    AuditAction.Executed,
                    fhirRequestContext.Method,
                    null /* resourceType */,
                    fhirRequestContext.Uri,
                    System.Net.HttpStatusCode.Forbidden,
                    fhirRequestContext.CorrelationId,
                    claims);
            }
            else
            {
                _auditLogger.DidNotReceiveWithAnyArgs().LogAudit(
                    AuditAction.Executed, action: null, resourceType: null, requestUri: null, statusCode: null, correlationId: null, claims: null);
            }
        }
    }
}
