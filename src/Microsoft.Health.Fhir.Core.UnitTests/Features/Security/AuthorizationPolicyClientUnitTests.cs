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
    public class AuthorizationPolicyClientUnitTests
    {
        private ClaimsPrincipal _claimsPrincipal = new ClaimsPrincipal();

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Read)]
        public void GivenAClaimWithRoleWithPermissionForReadAction_WhenPermissionIsChecked_ReturnsTrue(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(roleConfiguration);
            Assert.True(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Read).Result);
        }

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Write)]
        public void GivenAClaimWithRoleWithPermissionForWriteAction_WhenPermissionIsChecked_ReturnsTrue(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(roleConfiguration);
            Assert.True(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Write).Result);
        }

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void GivenAClaimWithRoleWithPermissionForHardDeleteAction_WhenPermissionIsChecked_ReturnsTrue(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(roleConfiguration);
            Assert.True(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.HardDelete).Result);
        }

        [Theory]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Read)]
        public void GivenAClaimWithRoleWithoutPermissionForReadAction_WhenPermissionIsChecked_ReturnsFalse(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(roleConfiguration);
            Assert.False(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Read).Result);
        }

        [Theory]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Write)]
        public void GivenAClaimWithRoleWithoutPermissionForWriteAction_WhenPermissionIsChecked_ReturnsFalse(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(roleConfiguration);
            Assert.False(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Write).Result);
        }

        [Theory]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void GivenAClaimWithRoleWithoutPermissionForHardDeleteAction_WhenPermissionIsChecked_ReturnsFalse(ClaimsPrincipal claimsPrincipal, IRoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(roleConfiguration);
            Assert.False(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.HardDelete).Result);
        }

        public static IEnumerable<object[]> GetCompatibleRoleDataForAction(ResourceAction action)
        {
            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action }, "role1"));
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.Write }, "role2"));
            yield return testData.TakeLast(2).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.HardDelete }, "role1", "role2"));
            yield return testData.TakeLast(2).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role3"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action }, "role1", "role2", "role3"));
            yield return testData.TakeLast(2).ToArray();
        }

        public static IEnumerable<object[]> GetIncompatibleRoleDataForAction(ResourceAction action)
        {
            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action }, "role3"));
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.Write }, "role6"));
            yield return testData.TakeLast(2).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action, ResourceAction.HardDelete }, "role3", "role4"));
            yield return testData.TakeLast(2).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role3"));
            testData.Add(GetRoleConfigurationForRoles(new List<ResourceAction> { action }, "role1", "role2", "role5"));
            yield return testData.TakeLast(2).ToArray();
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
