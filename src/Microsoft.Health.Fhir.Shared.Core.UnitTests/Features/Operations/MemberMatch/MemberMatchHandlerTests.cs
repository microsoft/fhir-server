// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.MemberMatch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.MemberMatch)]
    public class MemberMatchHandlerTests
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IMemberMatchService _memberMatchService;
        private readonly MemberMatchHandler _memberMatchHandler;

        public MemberMatchHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _memberMatchService = Substitute.For<IMemberMatchService>();

            _memberMatchHandler = new MemberMatchHandler(
                _authorizationService,
                _memberMatchService);
        }

        [Fact]
        public async Task GivenAValidRequest_WhenUserHasReadPermission_ThenReturnsSuccessfully()
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(DataActions.Read);
            _memberMatchService.FindMatch(coverage, patient, Arg.Any<CancellationToken>())
                .Returns(patient);

            // Act
            MemberMatchResponse response = await _memberMatchHandler.Handle(request, CancellationToken.None);

            // Assert
            Assert.NotNull(response);
            Assert.NotNull(response.Patient);
            Assert.Equal(patient, response.Patient);

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.Received(1).FindMatch(coverage, patient, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAValidRequest_WhenUserLacksReadPermission_ThenThrowsUnauthorizedFhirActionException()
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(DataActions.None);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(
                () => _memberMatchHandler.Handle(request, CancellationToken.None));

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.DidNotReceive().FindMatch(Arg.Any<ResourceElement>(), Arg.Any<ResourceElement>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAValidRequest_WhenUserHasWriteButNotReadPermission_ThenThrowsUnauthorizedFhirActionException()
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(DataActions.Write);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(
                () => _memberMatchHandler.Handle(request, CancellationToken.None));

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.DidNotReceive().FindMatch(Arg.Any<ResourceElement>(), Arg.Any<ResourceElement>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenANullRequest_WhenHandlerInvoked_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<System.ArgumentNullException>(
                () => _memberMatchHandler.Handle(null, CancellationToken.None));

            await _authorizationService.DidNotReceive().CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>());
            await _memberMatchService.DidNotReceive().FindMatch(Arg.Any<ResourceElement>(), Arg.Any<ResourceElement>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAValidRequest_WhenServiceThrowsMemberMatchMatchingException_ThenExceptionPropagates()
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);
            var expectedException = new MemberMatchMatchingException("No match found");

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(DataActions.Read);
            _memberMatchService.FindMatch(coverage, patient, Arg.Any<CancellationToken>())
                .Returns<ResourceElement>(_ => throw expectedException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<MemberMatchMatchingException>(
                () => _memberMatchHandler.Handle(request, CancellationToken.None));

            Assert.Equal(expectedException, exception);

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.Received(1).FindMatch(coverage, patient, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAValidRequest_WhenCancellationRequested_ThenCancellationTokenIsPassedToService()
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(DataActions.Read);
            _memberMatchService.FindMatch(coverage, patient, Arg.Any<CancellationToken>())
                .Returns<ResourceElement>(_ => throw new TaskCanceledException());

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => _memberMatchHandler.Handle(request, cts.Token));

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, cts.Token);
            await _memberMatchService.Received(1).FindMatch(coverage, patient, cts.Token);
        }

        [Fact]
        public async Task GivenAValidRequest_WhenUserHasReadAndWritePermission_ThenThrowsUnauthorizedFhirActionException()
        {
            // Arrange
            // The handler requires EXACTLY Read permission, not Read | Write
            // This is intentional - the operation is restrictive
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(DataActions.Read | DataActions.Write);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(
                () => _memberMatchHandler.Handle(request, CancellationToken.None));

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.DidNotReceive().FindMatch(Arg.Any<ResourceElement>(), Arg.Any<ResourceElement>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAValidRequest_WhenServiceThrowsGenericException_ThenExceptionPropagates()
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);
            var expectedException = new System.InvalidOperationException("Service error");

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(DataActions.Read);
            _memberMatchService.FindMatch(coverage, patient, Arg.Any<CancellationToken>())
                .Returns<ResourceElement>(_ => throw expectedException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<System.InvalidOperationException>(
                () => _memberMatchHandler.Handle(request, CancellationToken.None));

            Assert.Equal(expectedException, exception);

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.Received(1).FindMatch(coverage, patient, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAValidRequest_WhenAuthorizationServiceThrowsException_ThenExceptionPropagates()
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns<DataActions>(_ => throw new System.InvalidOperationException("Authorization failed"));

            // Act & Assert
            await Assert.ThrowsAsync<System.InvalidOperationException>(
                () => _memberMatchHandler.Handle(request, CancellationToken.None));

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.DidNotReceive().FindMatch(Arg.Any<ResourceElement>(), Arg.Any<ResourceElement>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(DataActions.Create)]
        [InlineData(DataActions.Update)]
        [InlineData(DataActions.Delete)]
        [InlineData(DataActions.HardDelete)]
        [InlineData(DataActions.Export)]
        [InlineData(DataActions.Create | DataActions.Update)]
        [InlineData(DataActions.Update | DataActions.Delete)]
        public async Task GivenAValidRequest_WhenUserHasOtherPermissionsButNotRead_ThenThrowsUnauthorizedFhirActionException(DataActions permissions)
        {
            // Arrange
            var patient = Samples.GetDefaultPatient();
            var coverage = Samples.GetDefaultCoverage();
            var request = new MemberMatchRequest(coverage, patient);

            _authorizationService.CheckAccess(DataActions.Read, Arg.Any<CancellationToken>())
                .Returns(permissions);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(
                () => _memberMatchHandler.Handle(request, CancellationToken.None));

            await _authorizationService.Received(1).CheckAccess(DataActions.Read, Arg.Any<CancellationToken>());
            await _memberMatchService.DidNotReceive().FindMatch(Arg.Any<ResourceElement>(), Arg.Any<ResourceElement>(), Arg.Any<CancellationToken>());
        }
    }
}
