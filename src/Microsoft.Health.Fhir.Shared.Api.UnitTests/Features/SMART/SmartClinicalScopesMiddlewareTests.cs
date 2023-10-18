﻿// -------------------------------------------------------------------------------------------------
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
                    new ScopeRestriction("Patient", DataActions.Read, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read, "patient"),
                },
            };
            yield return new object[]
            {
                "patient.Patient.read",
                "user.Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read, "patient"),
                    new ScopeRestriction("Observation", DataActions.Write, "user"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.read",
                "practitioner/Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read, "patient"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.rd",
                "practitioner/Observation.wr",
                new List<ScopeRestriction>()
                {
                },
            };
        }

        public static IEnumerable<object[]> GetTestScopes()
        {
            yield return new object[] { "patient/Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read, "patient") } };
            yield return new object[]
            {
                "patient/Patient.read patient/Observation.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read, "patient"),
                },
            };
            yield return new object[]
            {
                "patient.Patient.read user.Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read, "patient"),
                    new ScopeRestriction("Observation", DataActions.Write, "user"),
                },
            };

            yield return new object[]
            {
                "user.VisionPrescription.write user.all.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("VisionPrescription", DataActions.Write, "user"),
                    new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "user"),
                },
            };

            yield return new object[] { "user/*.*", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Write | DataActions.Export, "user") } };
            yield return new object[] { "user/Encounter.*", new List<ScopeRestriction>() { new ScopeRestriction("Encounter", DataActions.Read | DataActions.Write | DataActions.Export, "user") } };
            yield return new object[] { "user/all.*", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Write | DataActions.Export, "user") } };
            yield return new object[] { "user/all.all", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Write | DataActions.Export, "user") } };
            yield return new object[] { "system.all.all", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Write | DataActions.Export, "system") } };
            yield return new object[] { "patient.Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read, "patient") } };
            yield return new object[] { "patient.Patient.all", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Write | DataActions.Export, "patient") } };
            yield return new object[] { "patient.*.read", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient") } };
            yield return new object[] { "patient.all.read", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "patient") } };
            yield return new object[]
            {
                "patient$Patient.read practitioner/Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read, "patient"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.rd practitioner/Observation.wr",
                new List<ScopeRestriction>()
                {
                },
            };
            yield return new object[]
            {
                "User$Patient.read patient/Observation.wr",
                new List<ScopeRestriction>()
                {
                },
            };
            yield return new object[]
            {
                "patient/Patient.read launch/patient user/Observation.read offline_access openid user/Encounter.* fhirUser",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read, "user"),
                    new ScopeRestriction("Encounter", DataActions.Read | DataActions.Write | DataActions.Export, "user"),
                },
            };
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
