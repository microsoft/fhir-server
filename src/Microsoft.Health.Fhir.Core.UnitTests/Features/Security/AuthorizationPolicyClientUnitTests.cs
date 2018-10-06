// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    public class AuthorizationPolicyClientUnitTests
    {
        private ClaimsPrincipal _claimsPrincipal = new ClaimsPrincipal();

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Read)]
        public void HasPermissionAsync_Succeeds_With_Proper_Claims_For_ReadAction(ClaimsPrincipal claimsPrincipal, RoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(Options.Create(roleConfiguration));
            Assert.True(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Read).Result);
        }

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Write)]
        public void HasPermissionAsync_Succeeds_With_Proper_Claims_For_WriteAction(ClaimsPrincipal claimsPrincipal, RoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(Options.Create(roleConfiguration));
            Assert.True(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Write).Result);
        }

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void HasPermissionAsync_Succeeds_With_Proper_Claims_For_HardDeleteAction(ClaimsPrincipal claimsPrincipal, RoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(Options.Create(roleConfiguration));
            Assert.True(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.HardDelete).Result);
        }

        [Theory]
        [MemberData(nameof(GetInCompatibleRoleDataForAction), ResourceAction.Read)]
        public void HasPermissionAsync_Fails_With_Proper_Claims_For_ReadAction(ClaimsPrincipal claimsPrincipal, RoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(Options.Create(roleConfiguration));
            Assert.False(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Read).Result);
        }

        [Theory]
        [MemberData(nameof(GetInCompatibleRoleDataForAction), ResourceAction.Write)]
        public void HasPermissionAsync_Fails_With_Proper_Claims_For_WriteAction(ClaimsPrincipal claimsPrincipal, RoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(Options.Create(roleConfiguration));
            Assert.False(authPolicyClient.HasPermissionAsync(claimsPrincipal, ResourceAction.Write).Result);
        }

        [Theory]
        [MemberData(nameof(GetInCompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void HasPermissionAsync_Fails_With_Proper_Claims_For_HardDeleteAction(ClaimsPrincipal claimsPrincipal, RoleConfiguration roleConfiguration)
        {
            var authPolicyClient = new AuthorizationPolicyClient(Options.Create(roleConfiguration));
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

        public static IEnumerable<object[]> GetInCompatibleRoleDataForAction(ResourceAction action)
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

        private static RoleConfiguration GetRoleConfigurationForRoles(List<ResourceAction> resourceActions, params string[] roleNames)
        {
            var roleConfiguration = new RoleConfiguration();
            List<ResourcePermission> permissions = new List<ResourcePermission>();

            var roles = roleNames.Select(ra => new Role(ra, new List<ResourcePermission> { new ResourcePermission { Actions = resourceActions }, }));
            roleConfiguration.Roles = roles.ToList();
            return roleConfiguration;
        }
    }
}
