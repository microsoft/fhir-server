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
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    public class RoleBasedAuthorizationPolicyUnitTests
    {
        private ClaimsPrincipal _claimsPrincipal = new ClaimsPrincipal();
        private readonly IOptions<SecurityConfiguration> _securityOptions = Substitute.For<IOptions<SecurityConfiguration>>();
        private readonly SecurityConfiguration _securityConfiguration = new SecurityConfiguration();

        public RoleBasedAuthorizationPolicyUnitTests()
        {
            _securityOptions.Value.Returns(_securityConfiguration);
        }

        [Theory]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Read)]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.Write)]
        [MemberData(nameof(GetCompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void GivenAClaimWithRoleWithPermissionForCompatibleAction_WhenPermissionIsChecked_ReturnsTrue(ClaimsPrincipal claimsPrincipal, AuthorizationConfiguration authorizationConfiguration, ResourceAction action)
        {
            _securityConfiguration.Authorization = authorizationConfiguration;
            var authPolicyClient = new RoleBasedAuthorizationPolicy(_securityOptions);

            Assert.True(authPolicyClient.HasActionPermission(claimsPrincipal, action));
        }

        [Theory]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Read)]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.Write)]
        [MemberData(nameof(GetIncompatibleRoleDataForAction), ResourceAction.HardDelete)]
        public void GivenAClaimWithRoleWithoutPermissionForIncompatibleAction_WhenPermissionIsChecked_ReturnsFalse(ClaimsPrincipal claimsPrincipal, AuthorizationConfiguration authorizationConfiguration, ResourceAction action)
        {
            _securityConfiguration.Authorization = authorizationConfiguration;
            var authPolicyClient = new RoleBasedAuthorizationPolicy(_securityOptions);

            Assert.False(authPolicyClient.HasActionPermission(claimsPrincipal, action));
        }

        public static IEnumerable<object[]> GetCompatibleRoleDataForAction(ResourceAction action)
        {
            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action }, "role1"));
            testData.Add(action);
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action, ResourceAction.Write }, "role2"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action, ResourceAction.HardDelete }, "role1", "role2"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role3"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action }, "role1", "role2", "role3"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
        }

        public static IEnumerable<object[]> GetIncompatibleRoleDataForAction(ResourceAction action)
        {
            var testData = new List<object>();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action }, "role3"));
            testData.Add(action);
            yield return testData.ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action, ResourceAction.Write }, "role6"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role1", "role2"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action, ResourceAction.HardDelete }, "role3", "role4"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
            testData.Add(GetClaimsPrincipalForRoles("role3"));
            testData.Add(GetAuthorizationConfigurationForRoles(new HashSet<ResourceAction> { action }, "role1", "role2", "role5"));
            testData.Add(action);
            yield return testData.TakeLast(3).ToArray();
        }

        private static ClaimsPrincipal GetClaimsPrincipalForRoles(params string[] roles)
        {
            var claimsId = new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new List<ClaimsIdentity> { claimsId });
        }

        private static AuthorizationConfiguration GetAuthorizationConfigurationForRoles(HashSet<ResourceAction> resourceActions, params string[] roleNames)
        {
            var permissions = new HashSet<ResourcePermission>
            {
                new ResourcePermission(resourceActions),
            };

            var roles = roleNames.Select(ra => new Role() { Name = ra, ResourcePermissions = permissions }).ToHashSet();

            return new AuthorizationConfiguration
            {
                Roles = roles,
            };
        }
    }
}
