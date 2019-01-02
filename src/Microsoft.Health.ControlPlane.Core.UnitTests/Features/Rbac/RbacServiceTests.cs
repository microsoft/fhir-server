// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.ControlPlane.Core.UnitTests.Features.Rbac
{
    public class RbacServiceTests
    {
        private readonly IdentityProvider _identityProvider;
        private readonly RbacService _rbacService;
        private readonly IControlPlaneDataStore _controlPlaneDataStore;

        public RbacServiceTests()
        {
            _identityProvider = new IdentityProvider("aad", "https://login.microsoftonline.com/common", new List<string> { "test" }, "1");
            _controlPlaneDataStore = Substitute.For<IControlPlaneDataStore>();
            _controlPlaneDataStore.GetIdentityProviderAsync(_identityProvider.Name, Arg.Any<CancellationToken>()).Returns(_identityProvider);
            _controlPlaneDataStore.UpsertIdentityProviderAsync(_identityProvider, Arg.Any<CancellationToken>()).Returns(_identityProvider);

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
        public async void GivenAnIdentityProvider_WhenUpsertingIdentityProvider_ThenDataStoreIsCalled()
        {
            var identityProvider = await _rbacService.UpsertIdentityProviderAsync(_identityProvider, CancellationToken.None);

            Assert.Same(_identityProvider, identityProvider);
        }
    }
}
