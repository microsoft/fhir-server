// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
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
        public void GivenAClaimWithRoleWithCompatibleAction_WhenPermissionIsChecked_ReturnsTrue(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration, ResourceAction action)
        {
            var roleBasedAuthorizationPolicy = new RoleBasedAuthorizationPolicy(roleConfiguration);
            Assert.True(roleBasedAuthorizationPolicy.HasPermission(claimsPrincipal, action));
        }

        [Theory]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Read)]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Write)]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void GivenAClaimWithRoleWithoutCompatibleAction_WhenPermissionIsChecked_ReturnsFalse(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration, ResourceAction action)
        {
            var roleBasedAuthorizationPolicy = new RoleBasedAuthorizationPolicy(roleConfiguration);
            Assert.False(roleBasedAuthorizationPolicy.HasPermission(claimsPrincipal, action));
        }

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Read)]
        public void GivenAClaimWithCompatibleAction_WhenGettingApplicablePermissions_ReturnsPermission(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration, ResourceAction action)
        {
            var roleBasedAuthorizationPolicy = new RoleBasedAuthorizationPolicy(roleConfiguration);
            var applicableResourcePermissions = roleBasedAuthorizationPolicy.GetApplicableResourcePermissions(claimsPrincipal, action);

            Assert.NotEmpty(applicableResourcePermissions);
        }

        [Theory]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Read)]
        public void GivenAClaimWithIncompatibleAction_WhenGettingApplicablePermissions_ReturnsNoPermissions(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration, ResourceAction action)
        {
            var roleBasedAuthorizationPolicy = new RoleBasedAuthorizationPolicy(roleConfiguration);
            var applicableResourcePermissions = roleBasedAuthorizationPolicy.GetApplicableResourcePermissions(claimsPrincipal, action);

            Assert.Empty(applicableResourcePermissions);
        }

        public static IEnumerable<object[]> GetCompatibleRoleDataForAction(ResourceAction action)
        {
            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action }, "role1"));
            testData.Add(action);
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.Write }, "role2"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.HardDelete }, "role1", "role2"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role3"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action }, "role1", "role2", "role3"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
        }

        public static IEnumerable<object[]> GetIncompatibleRoleDataForAction(ResourceAction action)
        {
            ResourceAction incompatibleAction = ResourceAction.Read;
            switch (action)
            {
                case ResourceAction.Read:
                    incompatibleAction = ResourceAction.Write;
                    break;
                case ResourceAction.Write:
                    incompatibleAction = ResourceAction.HardDelete;
                    break;
                case ResourceAction.HardDelete:
                    incompatibleAction = ResourceAction.Read;
                    break;
            }

            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { incompatibleAction }, "role1"));
            testData.Add(action);
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action }, "role3"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
        }

        private static ClaimsPrincipal GetClaimsPrincipalForRoles(params string[] roles)
        {
            var claimsId = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new List<ClaimsIdentity> { claimsId });
        }

        private static IRoleConfiguration GetRoleConfigurationForRoles(List<ResourceAction> resourceActions, params string[] roleNames)
        {
            var roleConfiguration = Substitute.For<IRoleConfiguration>();
            List<ResourcePermission> permissions = new List<ResourcePermission>();
            var resourcePermission = new ResourcePermission();
            var actions = (List<ResourceAction>)resourcePermission.Actions;
            actions.AddRange(resourceActions);
            permissions.Add(resourcePermission);

            var roles = roleNames.Select(ra => new Role() { Name = ra, ResourcePermissions = permissions }).ToList();

            roleConfiguration.Roles.Returns(roles);
            return roleConfiguration;
        }
    }
}
