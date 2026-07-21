// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.MemberMatch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class MemberMatchHandlerTests
    {
        private readonly IAuthorizationService<DataActions> _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
        private readonly IMemberMatchService _memberMatchService = Substitute.For<IMemberMatchService>();
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor = new FhirRequestContextAccessor();

        [Theory]
        [InlineData("patient")]
        [InlineData("user")]
        [InlineData("system")]
        public async Task GivenSmartFineGrainedContext_WhenHandlingMemberMatch_ThenRequestIsForbidden(string scopeContext)
        {
            MemberMatchHandler handler = CreateHandler(
                applyFineGrainedAccessControl: true,
                enableSmartMemberMatchRestriction: true,
                new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, scopeContext));
            MemberMatchRequest request = CreateRequest();

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(
                () => handler.HandleAsync(request, CancellationToken.None));

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, CancellationToken.None);
            await _memberMatchService.DidNotReceiveWithAnyArgs().FindMatch(default, default, default);
        }

        [Fact]
        public async Task GivenNonSmartContext_WhenHandlingMemberMatch_ThenRequestIsAllowed()
        {
            MemberMatchHandler handler = CreateHandler(applyFineGrainedAccessControl: false, enableSmartMemberMatchRestriction: true);
            MemberMatchRequest request = CreateRequest();
            _memberMatchService.FindMatch(request.Coverage, request.Patient, CancellationToken.None).Returns(request.Patient);

            MemberMatchResponse response = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Same(request.Patient, response.Patient);
            await _authorizationService.Received(1).CheckAccess(DataActions.Read, CancellationToken.None);
            await _memberMatchService.Received(1).FindMatch(request.Coverage, request.Patient, CancellationToken.None);
        }

        [Theory]
        [InlineData("patient")]
        [InlineData("user")]
        [InlineData("system")]
        public async Task GivenSmartFineGrainedContextAndRestrictionDisabled_WhenHandlingMemberMatch_ThenPreviousBehaviorIsRestoredAndRequestIsAllowed(string scopeContext)
        {
            MemberMatchHandler handler = CreateHandler(
                applyFineGrainedAccessControl: true,
                enableSmartMemberMatchRestriction: false,
                new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, scopeContext));
            MemberMatchRequest request = CreateRequest();
            _memberMatchService.FindMatch(request.Coverage, request.Patient, CancellationToken.None).Returns(request.Patient);

            MemberMatchResponse response = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Same(request.Patient, response.Patient);
            await _authorizationService.Received(1).CheckAccess(DataActions.Read, CancellationToken.None);
            await _memberMatchService.Received(1).FindMatch(request.Coverage, request.Patient, CancellationToken.None);
        }

        [Fact]
        public async Task GivenNonSmartContextAndRestrictionDisabled_WhenHandlingMemberMatch_ThenNonSmartBehaviorIsUnchanged()
        {
            MemberMatchHandler handler = CreateHandler(applyFineGrainedAccessControl: false, enableSmartMemberMatchRestriction: false);
            MemberMatchRequest request = CreateRequest();
            _memberMatchService.FindMatch(request.Coverage, request.Patient, CancellationToken.None).Returns(request.Patient);

            MemberMatchResponse response = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Same(request.Patient, response.Patient);
            await _authorizationService.Received(1).CheckAccess(DataActions.Read, CancellationToken.None);
            await _memberMatchService.Received(1).FindMatch(request.Coverage, request.Patient, CancellationToken.None);
        }

        private MemberMatchHandler CreateHandler(
            bool applyFineGrainedAccessControl,
            bool enableSmartMemberMatchRestriction = true,
            params ScopeRestriction[] scopeRestrictions)
        {
            var requestContext = new FhirRequestContext(
                method: "POST",
                uriString: "http://localhost/$member-match",
                baseUriString: "http://localhost/",
                correlationId: "member-match-handler-test",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());

            requestContext.AccessControlContext.ApplyFineGrainedAccessControl = applyFineGrainedAccessControl;
            foreach (ScopeRestriction scopeRestriction in scopeRestrictions)
            {
                requestContext.AccessControlContext.AllowedResourceActions.Add(scopeRestriction);
            }

            _requestContextAccessor.RequestContext = requestContext;
            _authorizationService.CheckAccess(DataActions.Read, CancellationToken.None).Returns(DataActions.Read);

            var coreFeatures = new CoreFeatureConfiguration
            {
                EnableSmartMemberMatchRestriction = enableSmartMemberMatchRestriction,
            };

            return new MemberMatchHandler(
                _authorizationService,
                _memberMatchService,
                _requestContextAccessor,
                Options.Create(coreFeatures));
        }

        private static MemberMatchRequest CreateRequest()
        {
            return new MemberMatchRequest(
                Samples.GetDefaultCoverage(),
                Samples.GetDefaultPatient());
        }
    }
}
