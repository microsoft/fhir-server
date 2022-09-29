// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Smart;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using NSubstitute;
using Xunit;
using Claim = System.Security.Claims.Claim;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Smart
{
    public class SmartClinicalScopesMiddlewareTests
    {
        private readonly SmartClinicalScopesMiddleware _smartClinicalScopesMiddleware;

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

            var authorizationConfiguration = new AuthorizationConfiguration();
            authorizationConfiguration.Enabled = true;

            var scopesClaim = new Claim(authorizationConfiguration.ScopesClaim, scopes);
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, authorizationConfiguration);

            Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
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

            yield return new object[] { "user/*.*", new List<ScopeRestriction>() { new ScopeRestriction("*", DataActions.Read | DataActions.Write, "user") } };
            yield return new object[] { "user/Encounter.*", new List<ScopeRestriction>() { new ScopeRestriction("Encounter", DataActions.Read | DataActions.Write, "user") } };
            yield return new object[] { "patient.Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read, "patient") } };
            yield return new object[] { "patient.*.read", new List<ScopeRestriction>() { new ScopeRestriction("*", DataActions.Read, "patient") } };
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
    }
}
