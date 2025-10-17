// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Smart;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using Claim = System.Security.Claims.Claim;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Smart
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class SmartClinicalScopesMiddlewareTests
    {
        private readonly SmartClinicalScopesMiddleware _smartClinicalScopesMiddleware;
        private IAuthorizationService<DataActions> _authorizationService;
        private ILogger<SmartClinicalScopesMiddleware> _logger = Substitute.For<ILogger<SmartClinicalScopesMiddleware>>();

        public SmartClinicalScopesMiddlewareTests()
        {
            _smartClinicalScopesMiddleware = new SmartClinicalScopesMiddleware(
                httpContext => Task.CompletedTask, _logger);
        }

        [Theory]
        [MemberData(nameof(GetTestScopesAndRoles))]
        public async Task GivenSmartScopesSplitAcrossClaims_WhenInvoked_ThenScopeParsedandAddedtoContext(string scopes, string claims, ICollection<ScopeRestriction> expectedScopeRestrictions)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, claims);
            var rolesSmartUserClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim, rolesSmartUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
            }
        }

        [Theory]
        [MemberData(nameof(GetTestScopes))]
        public async Task GivenSmartScope_WhenInvoked_ThenScopeParsedandAddedtoContext(string scopes, ICollection<ScopeRestriction> expectedScopeRestrictions)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
            }
        }

        [Theory]
        [MemberData(nameof(GetTestScopes))]
        public async Task GivenSmartRawScope_WhenInvoked_ThenScopeParsedandAddedtoContext(string scopes, ICollection<ScopeRestriction> expectedScopeRestrictions)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            var rawScopesClaim = new Claim("raw_scope", scopes);
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { rawScopesClaim, rolesClaim, fhirUserClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

            Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
        }

        [Theory]
        [MemberData(nameof(GetMixedTestScopes))]
        public async Task GivenMixedSmartScope_WhenInvoked_ThenBadRequestIsThrown(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext();

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await Assert.ThrowsAsync<BadHttpRequestException>(() =>
                    _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService));
            }
        }

        [Theory]
        [InlineData("smartUser", true, true)]
        [InlineData("globalAdmin", true, false)]
        [InlineData("smartUser", false, false)]
        [InlineData("globalAdmin", false, false)]
        public async Task GivenSmartDataAction_WhenInvoked_ThenApplyFineGrainedAccessControlIsSet(string role, bool securityEnabled, bool expectedApplyFineGrainedAccessControl)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = securityEnabled;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, role);
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { rolesClaim, fhirUserClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

            Assert.Equal(expectedApplyFineGrainedAccessControl, fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl);
        }

        [Fact]
        public async Task GivenSmartDataAction_WhenFhirUserNotProvided_ThenBadRequestExceptionThrown()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            authorizationConfiguration.ErrorOnMissingFhirUserClaim = true;
            await LoadRoles(authorizationConfiguration);

            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { rolesClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            await Assert.ThrowsAsync<BadHttpRequestException>(() =>
                _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService));
        }

        [Fact]
        public async Task GivenAllDataActionExceptSmart_WhenScopesProvided_ThenScopeRestrictionsNotApplied()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            fhirConfiguration.Security.Enabled = true;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "globalAdmin");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, "patient.patient.read");
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                Assert.Empty(fhirRequestContext.AccessControlContext.AllowedResourceActions);
            }
        }

        [Fact]
        public async Task GivenFhirUserInExtensionClaim_WhenRequestMade_ThenFhirUserIsSaved()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.EnableSmartWithoutAuth = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.ExtensionFhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, "patient.patient.read");
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(new Uri("https://fhirServer/Patient/foo"), fhirRequestContext.AccessControlContext.FhirUserClaim);
            }
        }

        [Fact]
        public async Task GivenFhirUserAndExtensionFhirUserClaimsBothExist_WhenRequestMade_ThenFhirUserClaimIsUsed()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.EnableSmartWithoutAuth = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.ExtensionFhirUserClaim, "https://fhirServer/Patient/foo1");
            var extensionFhirUserClaim = new Claim(authorizationConfiguration.ExtensionFhirUserClaim, "https://fhirServer/Patient/foo2");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, "patient.patient.read");
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim, extensionFhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(new Uri("https://fhirServer/Patient/foo1"), fhirRequestContext.AccessControlContext.FhirUserClaim);
            }
        }

        public static IEnumerable<object[]> GetTestScopesAndRoles()
        {
            yield return new object[]
            {
                "patient/Patient.read",
                "patient/Observation.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "patient.Patient.read",
                "user.Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Write | DataActions.Create | DataActions.Delete | DataActions.Update, "user"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.read",
                "practitioner/Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.rd",
                "practitioner/Observation.wr",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Delete, "patient"),
                },
            };
        }

        public static IEnumerable<object[]> GetTestScopes()
        {
            yield return new object[] { "patient/Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[]
            {
                "patient/Patient.read patient/Observation.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "patient.Patient.read user.Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                },
            };

            yield return new object[]
            {
                "user.VisionPrescription.write user.all.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("VisionPrescription", DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                    new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Export | DataActions.Search, "user"),
                },
            };

            yield return new object[]
            {
                "user/*.*",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                },
            };

            yield return new object[] { "user/Encounter.*", new List<ScopeRestriction>() { new ScopeRestriction("Encounter", DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user") } };
            yield return new object[] { "user/all.*", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user") } };
            yield return new object[] { "user/all.all", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user") } };
            yield return new object[] { "system.all.all", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "system") } };
            yield return new object[] { "patient.Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[] { "patient.Patient.all", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "patient") } };
            yield return new object[] { "patient.*.read", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[] { "patient.all.read", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[]
            {
                "patient$Patient.read practitioner/Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.rd practitioner/Observation.wr",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Delete, "patient"),
                },
            };
            yield return new object[]
            {
                "patient/Patient.read launch/patient user/Observation.read offline_access openid user/Encounter.* fhirUser",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "user"),
                    new ScopeRestriction("Encounter", DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                },
            };

            // SMART v2 scope format tests
            yield return new object[] { "patient/Patient.rs", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient") } };
            yield return new object[] { "patient/Patient.r", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.ReadById, "patient") } };
            yield return new object[] { "patient/Patient.s", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Search | DataActions.Export, "patient") } };
            yield return new object[] { "patient/Patient.c", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Create, "patient") } };
            yield return new object[] { "patient/all.c", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Create, "patient") } };
            yield return new object[] { "patient.all.c", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Create, "patient") } };
            yield return new object[] { "patient/Patient.u", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Update, "patient") } };
            yield return new object[] { "patient/Patient.d", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Delete, "patient") } };
            yield return new object[] { "patient/Patient.cruds", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Create | DataActions.Update | DataActions.Delete | DataActions.ReadById | DataActions.Search | DataActions.Export, "patient") } };
            yield return new object[] { "user/*.rs", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.ReadById | DataActions.Search | DataActions.Export, "user") } };
            yield return new object[]
            {
                "patient/Patient.rs user/Observation.cud",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient"),
                    new ScopeRestriction("Observation", DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                },
            };

            // Test v1 vs v2 behavior: v1 .read includes search, v2 .r does not include search
            yield return new object[] { "patient/Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[]
            {
                "patient/Patient.s patient/Observation.r",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.ReadById, "patient"),
                },
            };

            // Test v1 vs v2 write behavior: v1 .write includes all write operations, v2 granular permissions
            yield return new object[] { "patient/Patient.write", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete, "patient") } };
            yield return new object[]
            {
                "patient/Patient.c user/Observation.cu",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Create, "patient"),
                    new ScopeRestriction("Observation", DataActions.Create | DataActions.Update, "user"),
                },
            };

            // SMART v2 granular scopes with search parameters

            yield return new object[]
            {
                "patient/Patient.rs?name=john",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john")),
                },
            };

            yield return new object[]
            {
                "patient/Observation.s?code=44501",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "44501")),
                },
            };

            // Multiple search parameters
            yield return new object[]
            {
                "patient/Patient.rs?name=john&gender=male",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john").Add("gender", "male")),
                },
            };

            // With _include parameter
            yield return new object[]
            {
                "patient/Observation.rs?code=http://loinc.org|55233-1&_include=Observation:subject",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "http://loinc.org|55233-1").Add("_include", "Observation:subject")),
                },
            };

                        // With _revinclude parameter
            yield return new object[]
            {
                "patient/Patient.rs?name=SMARTGivenName1&_revinclude=Observation:subject",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "SMARTGivenName1").Add("_revinclude", "Observation:subject")),
                },
            };

                        // Multiple scopes with search parameters
            yield return new object[]
            {
                "patient/Patient.rs?name=john patient/Observation.s?code=44501&status=final",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john")),
                    new ScopeRestriction("Observation", DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "44501").Add("status", "final")),
                },
            };

                        // Complex example with all SMART v2 granular permissions and search parameters
            yield return new object[]
            {
                "user/Patient.cruds?name=Smith&birthdate=ge2000 user/Observation.rs?category=vital-signs",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Create | DataActions.ReadById | DataActions.Update | DataActions.Delete | DataActions.Search | DataActions.Export, "user", new Hl7.Fhir.Rest.SearchParams("name", "Smith").Add("birthdate", "ge2000")),
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "user", new Hl7.Fhir.Rest.SearchParams("category", "vital-signs")),
                },
            };

            // System scope with search parameters
            yield return new object[]
            {
                "system/Patient.rs?active=true",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "system", new Hl7.Fhir.Rest.SearchParams("active", "true")),
                },
            };
        }

        public static IEnumerable<object[]> GetMixedTestScopes()
        {
            yield return new object[] { "patient/Patient.read patient/Observation.r" };
            yield return new object[] { "patient.Patient.read user.Observation.cr" };
            yield return new object[] { "patient$Patient.rd patient/Observation.write" };
            yield return new object[] { "patient$Patient.read patient/Observation.cr" };
            yield return new object[] { "patient/Patient.read launch/patient user/Observation.read offline_access openid user/Encounter.r fhirUser" };
            yield return new object[] { "patient/Patient.rs user/Observation.cud user/Encounter.write" };
            yield return new object[] { "patient/Patient.read user/Observation.c" };
            yield return new object[] { "patient/Patient.all user/Observation.r" };
            yield return new object[] { "patient/Patient.write user/Observation.u" };
            yield return new object[] { "patient/Patient.read user/Observation.d" };
            yield return new object[] { "patient/Patient.read patient/Patient.cruds" };
            yield return new object[] { "system/Patient.write user/Patient.r" };
            yield return new object[] { "patient/Patient.* user/Observation.d" };
            yield return new object[] { "patient/Patient.all patient/Patient.cruds" };
            yield return new object[] { "system/Patient.all user/Patient.r" };
            yield return new object[] { "system/Patient.* user/Patient.r" };
        }

        private static async Task<AuthorizationConfiguration> LoadRoles(AuthorizationConfiguration authConfig)
        {
            var roles = new
            {
                roles = new[]
                {
                    new
                    {
                        name = "smartUser",
                        dataActions = new[] { "*", "smart" },
                        notDataActions = new string[] { },
                        scopes = new[] { "/" },
                    },
                    new
                    {
                        name = "globalAdmin",
                        dataActions = new[] { "*" },
                        notDataActions = new string[] { },
                        scopes = new[] { "/" },
                    },
                },
            };

            IFileProvider fileProvider = Substitute.For<IFileProvider>();
            var hostEnvironment = Substitute.For<IHostEnvironment>();
            hostEnvironment.ContentRootFileProvider
                .GetFileInfo("roles.json")
                .CreateReadStream()
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes(JObject.FromObject(roles).ToString())));

            authConfig.ScopesClaim = new[] { "scope", "roles" };

            var roleLoader = new RoleLoader(authConfig, hostEnvironment);
            await roleLoader.StartAsync(CancellationToken.None);
            return authConfig;
        }
    }
}
