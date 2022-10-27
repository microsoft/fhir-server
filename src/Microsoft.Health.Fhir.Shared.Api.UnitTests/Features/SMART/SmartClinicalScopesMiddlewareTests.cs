// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Specification.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Smart;
using Microsoft.Health.Fhir.Core.Configs;
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

        public SmartClinicalScopesMiddlewareTests()
        {
            _smartClinicalScopesMiddleware = new SmartClinicalScopesMiddleware(
                httpContext => Task.CompletedTask);
        }

        [Theory]
        [MemberData(nameof(GetTestScopes))]
        public async Task GivenSMARTScope_WhenInvoked_ThenScopeParsedandAddedtoContext(string scopes, ICollection<ScopeRestriction> expectedScopeRestrictions)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");
            var scopesClaim = new Claim(authorizationConfiguration.ScopesClaim, scopes);
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

            Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
        }

        [Theory]
        [InlineData("smartUser", true)]
        [InlineData("globalAdmin", false)]
        public async Task GivenSmartUserRole_WhenInvoked_ThenApplyFineGrainedAccessControlIsSet(string role, bool expectedApplyFineGrainedAccessControl)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
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
        public async Task GivenSmartUserRole_WhenFhirUserNotProvided_ThenBadRequestExceptionThrown()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
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
        public async Task GivenAdminUserRole_WhenScopesProvided_ThenScopeRestrictionsNotApplied()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "globalAdmin");
            var scopesClaim = new Claim(authorizationConfiguration.ScopesClaim, "patient.patient.read");
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
            Assert.Empty(fhirRequestContext.AccessControlContext.AllowedResourceActions);
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

            yield return new object[] { "user/*.*", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Write, "user") } };
            yield return new object[] { "user/Encounter.*", new List<ScopeRestriction>() { new ScopeRestriction("Encounter", DataActions.Read | DataActions.Write, "user") } };
            yield return new object[] { "user/all.*", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Write, "user") } };
            yield return new object[] { "user/all.all", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Write, "user") } };
            yield return new object[] { "patient.Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read, "patient") } };
            yield return new object[] { "patient.Patient.all", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Write, "patient") } };
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
                    new ScopeRestriction("Encounter", DataActions.Read | DataActions.Write, "user"),
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

            var roleLoader = new RoleLoader(authConfig, hostEnvironment);
            await roleLoader.StartAsync(CancellationToken.None);
            return authConfig;
        }
    }
}
