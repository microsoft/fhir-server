// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security.Authorization
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class RoleBasedFhirAuthorizationServiceTests
    {
        private readonly RoleBasedFhirAuthorizationService _roleBasedFhirAuthorizationService;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly AuthorizationConfiguration _authorizationConfiguration;

        public RoleBasedFhirAuthorizationServiceTests()
        {
            var fhirConfiguration = new FhirServerConfiguration();
            _authorizationConfiguration = fhirConfiguration.Security.Authorization;
            _authorizationConfiguration.Enabled = true;
            List<Role> roles = new List<Role>();
            roles.Add(new Role("Read", DataActions.Read, "/"));
            roles.Add(new Role("Write", DataActions.Write, "/"));
            _authorizationConfiguration.Roles = roles;

            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            _roleBasedFhirAuthorizationService = new RoleBasedFhirAuthorizationService(
                _authorizationConfiguration, _fhirRequestContextAccessor);
        }

        [Fact]
        public async Task GivenUserReadDA_WhenInvoked_ForReadDA_NoSMARTScope_ThenReturnedReadDataAction()
        {
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = false;

            var claims = new List<Claim>();
            claims.Add(new Claim("roles", "Read"));
            var expectedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            defaultFhirRequestContext.Principal = expectedPrincipal;
            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);

            var result = await _roleBasedFhirAuthorizationService.CheckAccess(DataActions.Read, CancellationToken.None);
            Assert.Equal(DataActions.Read, result);
        }

        [Fact]
        public async Task GivenUserReadDA_WhenInvoked_ForWriteDA_NoSMARTScope_ThenReturnedNoneDataAction()
        {
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = false;

            var claims = new List<Claim>();
            claims.Add(new Claim("roles", "Read"));
            var expectedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            defaultFhirRequestContext.Principal = expectedPrincipal;
            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);

            var result = await _roleBasedFhirAuthorizationService.CheckAccess(DataActions.Write, CancellationToken.None);
            Assert.Equal(DataActions.None, result);
        }

        [Fact]
        public async Task GivenUserReadDA_WhenInvokedForPatientRead_SMARTScopePatientRead_ThenReturnedReadDataAction()
        {
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;

            var claims = new List<Claim>();
            claims.Add(new Claim("roles", "Read"));
            var expectedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            defaultFhirRequestContext.ResourceType = KnownResourceTypes.Patient;
            defaultFhirRequestContext.Principal = expectedPrincipal;

            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);
            _fhirRequestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"));

            var result = await _roleBasedFhirAuthorizationService.CheckAccess(DataActions.Read, CancellationToken.None);
            Assert.Equal(DataActions.Read, result);
        }

        [Fact]
        public async Task GivenUserReadDA_WhenInvokedForPatientRead_SMARTScopeMedicationRead_ThenReturnedNoneDataAction()
        {
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;

            var claims = new List<Claim>();
            claims.Add(new Claim("roles", "Read"));
            var expectedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            defaultFhirRequestContext.ResourceType = KnownResourceTypes.Patient;
            defaultFhirRequestContext.Principal = expectedPrincipal;

            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);
            _fhirRequestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(KnownResourceTypes.Medication, DataActions.Read, "user1"));

            var result = await _roleBasedFhirAuthorizationService.CheckAccess(DataActions.Read, CancellationToken.None);
            Assert.Equal(DataActions.None, result);
        }

        [Fact]
        public async Task GivenUserWriteDA_WhenInvokedForPatientWrite_SMARTScopePatientRead_ThenReturnedNoneDataAction()
        {
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;

            var claims = new List<Claim>();
            claims.Add(new Claim("roles", "Write"));
            var expectedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            defaultFhirRequestContext.ResourceType = KnownResourceTypes.Patient;
            defaultFhirRequestContext.Principal = expectedPrincipal;

            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);
            _fhirRequestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"));

            var result = await _roleBasedFhirAuthorizationService.CheckAccess(DataActions.Write, CancellationToken.None);
            Assert.Equal(DataActions.None, result);
        }

        [Fact]
        public async Task GivenUserWriteDA_WhenInvokedForPatientWrite_SMARTScopePatientReadAndWrite_ThenReturnedWriteDataAction()
        {
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;

            var claims = new List<Claim>();
            claims.Add(new Claim("roles", "Write"));
            var expectedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            defaultFhirRequestContext.ResourceType = KnownResourceTypes.Patient;
            defaultFhirRequestContext.Principal = expectedPrincipal;

            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);
            _fhirRequestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"));
            _fhirRequestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Write, "user1"));

            var result = await _roleBasedFhirAuthorizationService.CheckAccess(DataActions.Write, CancellationToken.None);
            Assert.Equal(DataActions.Write, result);
        }

        [Fact]
        public async Task GivenUserReadDA_WhenInvokedForPatientRead_SMARTScopeAllResourcesRead_ThenReturnedReadDataAction()
        {
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;

            var claims = new List<Claim>();
            claims.Add(new Claim("roles", "Read"));
            var expectedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            defaultFhirRequestContext.ResourceType = KnownResourceTypes.Patient;
            defaultFhirRequestContext.Principal = expectedPrincipal;

            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);
            _fhirRequestContextAccessor.RequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "user1"));

            var result = await _roleBasedFhirAuthorizationService.CheckAccess(DataActions.Read, CancellationToken.None);
            Assert.Equal(DataActions.Read, result);
        }
    }
}
