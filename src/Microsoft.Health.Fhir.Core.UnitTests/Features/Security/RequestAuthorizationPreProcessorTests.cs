// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Claims;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Get;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    public class RequestAuthorizationPreProcessorTests
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        private readonly IOptions<SecurityConfiguration> _securityOptions = Substitute.For<IOptions<SecurityConfiguration>>();
        private readonly SecurityConfiguration _securityConfiguration = Substitute.For<SecurityConfiguration>();
        private readonly ClaimsPrincipal _claimsPrincipal = Substitute.For<ClaimsPrincipal>();
        private readonly IAuthorizationPolicy _authorizationPolicy = Substitute.For<IAuthorizationPolicy>();

        public RequestAuthorizationPreProcessorTests()
        {
            _fhirRequestContext.RequestType.Returns(new Coding("System", "TestRequestType"));
            _fhirRequestContext.RequestSubType = new Coding("System", "TestRequestSubType");
            _fhirRequestContext.Uri.Returns(new Uri("https://fhirtest/fhir?count=100"));
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);
            _fhirRequestContextAccessor.FhirRequestContext.Principal.Returns(_claimsPrincipal);
            _securityConfiguration.Authorization.Enabled = true;
            _securityOptions.Value.Returns(_securityConfiguration);
        }

        private GetResourceRequest GetResourceRequest => new GetResourceRequest("Observation", "1234");

        [Fact]
        public async Task GivenARequest_WhenAuthorizationDisabled_ThenGetApplicableResourcePermissionsShouldNotBeCalled()
        {
            _securityConfiguration.Authorization.Enabled = false;
            var preProcessor = new RequestAuthorizationPreProcessor<GetResourceRequest>(_authorizationPolicy, _fhirRequestContextAccessor, _securityOptions);
            await preProcessor.Process(GetResourceRequest, CancellationToken.None);
            _authorizationPolicy.DidNotReceive().GetApplicableResourcePermissions(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceAction>());
        }

        [Fact]
        public async Task GivenARequest_WhenAuthorizationEnabled_ThenGetApplicableResourcePermissionsShouldBeCalled()
        {
            var preProcessor = new RequestAuthorizationPreProcessor<GetResourceRequest>(_authorizationPolicy, _fhirRequestContextAccessor, _securityOptions);
            await preProcessor.Process(GetResourceRequest, CancellationToken.None);
            _authorizationPolicy.Received(1).GetApplicableResourcePermissions(_fhirRequestContextAccessor.FhirRequestContext.Principal, ResourceAction.Read);
        }
    }
}
