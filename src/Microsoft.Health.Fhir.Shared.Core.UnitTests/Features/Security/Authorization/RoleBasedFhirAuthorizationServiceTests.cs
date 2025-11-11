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
            roles.Add(new Role("Create", DataActions.Create, "/"));
            roles.Add(new Role("ReadById", DataActions.ReadById, "/"));
            roles.Add(new Role("Update", DataActions.Update, "/"));
            roles.Add(new Role("Delete", DataActions.Delete, "/"));
            roles.Add(new Role("Search", DataActions.Search, "/"));
            _authorizationConfiguration.Roles = roles;

            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            _roleBasedFhirAuthorizationService = new RoleBasedFhirAuthorizationService(
                _authorizationConfiguration, _fhirRequestContextAccessor);
        }

        public static IEnumerable<object[]> GetAuthorizationTestData()
        {
            // Each test case sends the following parameters:
            // string testDescription, bool applyFineGrained (smart), string roleClaim, string resourceType, DataActions requestedAction,
            // List<ScopeRestriction> allowedResourceActions, DataActions expected

            // 1. No SMART scope, Read requested. Expect Read.
            yield return new object[]
            {
                "No SMART: Read action returns Read",
                false, // applyFineGrained
                "Read", // roleClaim
                null,   // resourceType is not needed
                DataActions.Read,
                new List<ScopeRestriction>(), // no allowed scopes
                DataActions.Read,
            };

            // 2. No SMART scope, Write requested with role Read. Expect None.
            yield return new object[]
            {
                "No SMART: Write action with Read role returns None",
                false,
                "Read",
                null,
                DataActions.Write,
                new List<ScopeRestriction>(),
                DataActions.None,
            };

            // 3. With SMART scope V1: For PatientRead, allowed scope for Patient with Read is present.
            yield return new object[]
            {
                "SMART: For Patient resource, allowed Patient Read returns Read",
                true,
                "Read",
                KnownResourceTypes.Patient,
                DataActions.Read,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"),
                },
                DataActions.Read,
            };

            // 4. With SMART scope V1: For PatientRead, allowed scope for Medication (not matching) returns None.
            yield return new object[]
            {
                "SMART: For Patient resource, allowed Medication Read returns None",
                true,
                "Read",
                KnownResourceTypes.Patient,
                DataActions.Read,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Medication, DataActions.Read, "user1"),
                },
                DataActions.None,
            };

            // 5. With SMART scope V1: For Patient Write, if only Patient Read is allowed, then Write returns None.
            yield return new object[]
            {
                "SMART: For Patient Write, allowed only Patient Read returns None",
                true,
                "Write",
                KnownResourceTypes.Patient,
                DataActions.Write,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"),
                },
                DataActions.None,
            };

            // 6. With SMART scope V1: For Patient Write, allowed scopes include both Read and Write, so Write returns Write.
            yield return new object[]
            {
                "SMART: For Patient Write, allowed Patient Read and Write returns Write",
                true,
                "Write",
                KnownResourceTypes.Patient,
                DataActions.Write,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"),
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Write, "user1"),
                },
                DataActions.Write,
            };

            // 7. With SMART scope V1: For Patient Write, if only Observation Write is allowed then returns None.
            yield return new object[]
            {
                "SMART: For Patient Write, allowed Observation Write (mismatch) returns None",
                true,
                "Write",
                KnownResourceTypes.Patient,
                DataActions.Write,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Write, "user1"),
                },
                DataActions.None,
            };

            // 8. With SMART scope V1: For Patient Read, if allowed All is provided, then Read returns Read.
            yield return new object[]
            {
                "SMART: For Patient Read with all resources allowed returns Read",
                true,
                "Read",
                KnownResourceTypes.Patient,
                DataActions.Read,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.All, DataActions.Read, "user1"),
                },
                DataActions.Read,
            };

            // 9. With SMART scope V2: For PatientReadById, allowed scope for Patient with ReadById is present.
            yield return new object[]
            {
                "SMART: For Patient resource, allowed Patient ReadById returns ReadById",
                true,
                "ReadById",
                KnownResourceTypes.Patient,
                DataActions.ReadById,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.ReadById, "user1"),
                },
                DataActions.ReadById,
            };

            // 10. With SMART scope V2: For PatientReadById, allowed scope for Medication (not matching) returns None.
            yield return new object[]
            {
                "SMART: For Patient resource, allowed Medication Read returns None",
                true,
                "ReadById",
                KnownResourceTypes.Patient,
                DataActions.ReadById,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Medication, DataActions.ReadById, "user1"),
                },
                DataActions.None,
            };

            // 11. With SMART scope V2: For Patient Create, if only Patient Read is allowed, then Create returns None.
            yield return new object[]
            {
                "SMART: For Patient Create, allowed only Patient Read returns None",
                true,
                "Create",
                KnownResourceTypes.Patient,
                DataActions.Create,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"),
                },
                DataActions.None,
            };

            // 12. With SMART scope V2: For Patient Update, if only Patient Read is allowed, then Create returns None.
            yield return new object[]
            {
                "SMART: For Patient Update, allowed only Patient Read returns None",
                true,
                "Update",
                KnownResourceTypes.Patient,
                DataActions.Update,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read, "user1"),
                },
                DataActions.None,
            };

            // 13. With SMART scope V2: For Patient Create, if only Patient Update is allowed, then Create returns None.
            yield return new object[]
            {
                "SMART: For Patient Create, allowed only Patient Update returns None",
                true,
                "Create",
                KnownResourceTypes.Patient,
                DataActions.Create,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Update, "user1"),
                },
                DataActions.None,
            };

            // 14. With SMART scope V2: For Patient Update, if only Patient Create is allowed, then Update returns None.
            yield return new object[]
            {
                "SMART: For Patient Update, allowed only Patient Create returns None",
                true,
                "Update",
                KnownResourceTypes.Patient,
                DataActions.Update,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Create, "user1"),
                },
                DataActions.None,
            };

            // 15. With SMART scope V2: For Patient Create, allowed scopes include both Create and Update, so Create returns Create.
            yield return new object[]
            {
                "SMART: For Patient Create, allowed Patient Create and Update returns Create",
                true,
                "Create",
                KnownResourceTypes.Patient,
                DataActions.Create,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Update, "user1"),
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Create, "user1"),
                },
                DataActions.Create,
            };

            // 16. With SMART scope V2: For Patient Update, allowed scopes include both Create and Update, so Update returns Update.
            yield return new object[]
            {
                "SMART: For Patient Update, allowed Patient Create and Update returns Update",
                true,
                "Update",
                KnownResourceTypes.Patient,
                DataActions.Update,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Update, "user1"),
                    new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Create, "user1"),
                },
                DataActions.Update,
            };

            // 17. With SMART scope V2: For PatientCreate, if only Observation Update is allowed then returns None.
            yield return new object[]
            {
                "SMART: For Patient Create, allowed Observation Update (mismatch) returns None",
                true,
                "Create",
                KnownResourceTypes.Patient,
                DataActions.Update,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Update, "user1"),
                },
                DataActions.None,
            };

            // 18. With SMART scope V2: For PatientUpdate, if only Observation Create is allowed then returns None.
            yield return new object[]
            {
                "SMART: For Patient Update, allowed Observation Create (mismatch) returns None",
                true,
                "Update",
                KnownResourceTypes.Patient,
                DataActions.Update,
                new List<ScopeRestriction>
                {
                    new ScopeRestriction(KnownResourceTypes.Observation, DataActions.Create, "user1"),
                },
                DataActions.None,
            };
        }

        [Theory]
        [MemberData(nameof(GetAuthorizationTestData))]
        public async Task CombinedAuthorizationTests(
            string testDescription,
            bool applyFineGrained,
            string roleClaim,
            string resourceType,
            DataActions requestedAction,
            List<ScopeRestriction> allowedResourceActions,
            DataActions expected)
        {
            // Arrange
            var defaultFhirRequestContext = new DefaultFhirRequestContext();
            defaultFhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = applyFineGrained;
            var claims = new List<Claim>();
            claims.Add(new Claim("roles", roleClaim));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

            // Set resource type if provided; if null, leave it as the default.
            if (!string.IsNullOrEmpty(resourceType))
            {
                defaultFhirRequestContext.ResourceType = resourceType;
            }

            defaultFhirRequestContext.Principal = principal;

            _fhirRequestContextAccessor.RequestContext.Returns(defaultFhirRequestContext);

            // Clear and set allowed scopes.
            defaultFhirRequestContext.AccessControlContext.AllowedResourceActions.Clear();
            foreach (var scope in allowedResourceActions)
            {
                defaultFhirRequestContext.AccessControlContext.AllowedResourceActions.Add(scope);
            }

            // Act
            var result = await _roleBasedFhirAuthorizationService.CheckAccess(requestedAction, CancellationToken.None);

            // Assert
            Assert.True(expected == result, testDescription);
        }
    }
}
