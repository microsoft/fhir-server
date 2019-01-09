// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.ControlPlane.Core.Features.Rbac.Roles;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.ControlPlane.Core.UnitTests.Features.Rbac
{
    public class RbacServiceTests
    {
        private readonly IdentityProvider _identityProvider;
        private readonly Role _role;
        private readonly RbacService _rbacService;
        private readonly IControlPlaneDataStore _controlPlaneDataStore;

        public RbacServiceTests()
        {
            _identityProvider = new IdentityProvider("aad", "https://login.microsoftonline.com/common", new List<string> { "test" }, "1");

            IList<ResourcePermission> resourcePermissions = new List<ResourcePermission>();
            resourcePermissions.Add(new ResourcePermission(new List<ResourceAction> { ResourceAction.Read }));
            _role = new Role("clinician", resourcePermissions, "1");

            _controlPlaneDataStore = Substitute.For<IControlPlaneDataStore>();
            _controlPlaneDataStore.GetIdentityProviderAsync(_identityProvider.Name, Arg.Any<CancellationToken>()).Returns(_identityProvider);
            _controlPlaneDataStore.UpsertIdentityProviderAsync(_identityProvider, Arg.Any<CancellationToken>()).Returns(_identityProvider);

            _controlPlaneDataStore.GetRoleAsync(_role.Name, Arg.Any<CancellationToken>()).Returns(_role);
            _controlPlaneDataStore.UpsertRoleAsync(_role, Arg.Any<CancellationToken>()).Returns(_role);
            _controlPlaneDataStore.DeleteRoleAsync(_role.Name, Arg.Any<CancellationToken>()).Returns("success");

            _rbacService = new RbacService(_controlPlaneDataStore);
        }

        [Fact]
        public async void GivenAName_WhenGettingIdentityProvider_ThenDataStoreIsCalled()
        {
            var identityProviderName = "aad";

            var identityProvider = await _rbacService.GetIdentityProviderAsync(identityProviderName, CancellationToken.None);

            Assert.Same(_identityProvider, identityProvider);
        }

        [Fact]
        public async void GivenAName_WhenGettingRoles_ThenDataStoreIsCalled()
        {
            var roleName = "clinician";

            var role = await _rbacService.GetRoleAsync(roleName, CancellationToken.None);

            Assert.Same(_role, role);
        }

        [Fact]
        public async void GivenAName_WhenUpsertingRoles_ThenDataStoreIsCalled()
        {
            var role = await _rbacService.UpsertRoleAsync(_role, CancellationToken.None);

            Assert.Same(_role, role);
        }

        [Fact]
        public async void GivenAName_WhenDeletingRoles_ThenDataStoreIsCalled()
        {
            var status = await _rbacService.DeleteRoleAsync(_role.Name, CancellationToken.None);

            Assert.Equal("success", status.ToString());
        }

        [Fact]
        public async void GivenAnIdentityProvider_WhenUpsertingIdentityProvider_ThenDataStoreIsCalled()
        {
            var identityProvider = await _rbacService.UpsertIdentityProviderAsync(_identityProvider, CancellationToken.None);

            Assert.Same(_identityProvider, identityProvider);
        }
    }
}
