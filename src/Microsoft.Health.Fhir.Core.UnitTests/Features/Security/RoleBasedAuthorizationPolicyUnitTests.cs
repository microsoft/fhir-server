// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    public class RoleBasedAuthorizationPolicyUnitTests
    {
        private ClaimsPrincipal _claimsPrincipal = new ClaimsPrincipal();

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Read)]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Write)]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void GivenAClaimWithRoleWithPermissionForCompatibleAction_WhenPermissionIsChecked_ReturnsTrue(ClaimsPrincipal claimsPrincipal, AuthorizationConfiguration authorizationConfiguration, ResourceAction action)
        {
            var authPolicyClient = new RoleBasedAuthorizationPolicy(authorizationConfiguration);
            Assert.True(authPolicyClient.HasPermission(claimsPrincipal, action));
        }

        [Theory]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Read)]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Write)]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void GivenAClaimWithRoleWithoutPermissionForIncompatibleAction_WhenPermissionIsChecked_ReturnsFalse(ClaimsPrincipal claimsPrincipal, AuthorizationConfiguration authorizationConfiguration, ResourceAction action)
        {
            var authPolicyClient = new RoleBasedAuthorizationPolicy(authorizationConfiguration);
            Assert.False(authPolicyClient.HasPermission(claimsPrincipal, action));
        }

        public static IEnumerable<object[]> GetCompatibleRoleDataForAction(ResourceAction action)
        {
            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action }, "role1"));
            testData.Add(action);
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.Write }, "role2"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.HardDelete }, "role1", "role2"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role3"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action }, "role1", "role2", "role3"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
        }

        public static IEnumerable<object[]> GetIncompatibleRoleDataForAction(ResourceAction action)
        {
            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action }, "role3"));
            testData.Add(action);
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.Write }, "role6"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.HardDelete }, "role3", "role4"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role3"));
            testData.Add(GetAuthorizationConfigurationForRoles(new List<ResourceAction> { action }, "role1", "role2", "role5"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
        }

        private static ClaimsPrincipal GetClaimsPrincipalForRoles(params string[] roles)
        {
            var claimsId = new ClaimsIdentity(roles.Select(r => new Claim("roles", r)));
            return new ClaimsPrincipal(new List<ClaimsIdentity> { claimsId });
        }

        private static AuthorizationConfiguration GetAuthorizationConfigurationForRoles(List<ResourceAction> resourceActions, params string[] roleNames)
        {
            var permissions = new List<ResourcePermission>
            {
                new ResourcePermission(resourceActions),
            };

            var authConfiguration = new AuthorizationConfiguration();

            foreach (var name in roleNames)
            {
                authConfiguration.Roles.Add(GetRole(name, permissions));
            }

            return authConfiguration;
        }

        private static Role GetRole(string name, IList<ResourcePermission> resourcePermissions)
        {
            var role = Substitute.For<Role>();
            role.Name.Returns(name);
            role.ResourcePermissions.Returns(resourcePermissions);

            role.Validate(Arg.Any<ValidationContext>()).Returns(Enumerable.Empty<ValidationResult>());

            return role;
        }
    }
}
