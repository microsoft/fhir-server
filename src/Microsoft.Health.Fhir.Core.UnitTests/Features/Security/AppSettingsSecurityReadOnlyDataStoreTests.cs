// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Security;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    public class AppSettingsSecurityReadOnlyDataStoreTests
    {
        private readonly IOptions<RoleConfiguration> _roleConfigurationOptions = Substitute.For<IOptions<RoleConfiguration>>();
        private readonly RoleConfiguration _roleConfiguration = Substitute.For<RoleConfiguration>();

        public AppSettingsSecurityReadOnlyDataStoreTests()
        {
            _roleConfigurationOptions.Value.Returns(_roleConfiguration);
        }

        [Fact]
        public async void GivenAnAppSettingsSecurityDataStore_WhenGettingAllWithMultipleRoles_ThenCorrectNumberReturned()
        {
            var appSettingsSecurityDataStore = new AppSettingsSecurityReadOnlyDataStore(_roleConfigurationOptions);

            _roleConfiguration.Roles.Returns(new List<Role> { new Role(), new Role(), });

            var results = await appSettingsSecurityDataStore.GetAllRolesAsync(CancellationToken.None);

            Assert.NotEmpty(results);
            Assert.Equal(2, results.Count());
        }

        [Fact]
        public async void GivenAnAppSettingsSecurityDataStore_WhenGettingAllWithNoRoles_ThenCorrectNumberReturned()
        {
            var appSettingsSecurityDataStore = new AppSettingsSecurityReadOnlyDataStore(_roleConfigurationOptions);

            _roleConfiguration.Roles.Returns(new List<Role>());

            var results = await appSettingsSecurityDataStore.GetAllRolesAsync(CancellationToken.None);

            Assert.Empty(results);
        }

        [Fact]
        public async void GivenAnAppSettingsSecurityDataStore_WhenGettingOneRoleWithMultipleRoles_ThenCorrectRoleReturned()
        {
            var appSettingsSecurityDataStore = new AppSettingsSecurityReadOnlyDataStore(_roleConfigurationOptions);

            _roleConfiguration.Roles.Returns(new List<Role> { new Role { Name = "role1", Version = "abc" }, new Role { Name = "role2", Version = "def" }, });

            var result = await appSettingsSecurityDataStore.GetRoleAsync("role1", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("abc", result.Version);
        }

        [Fact]
        public async void GivenAnAppSettingsSecurityDataStore_WhenGettingMissingRoleWithMultipleRoles_ThenExceptionIsThrown()
        {
            var appSettingsSecurityDataStore = new AppSettingsSecurityReadOnlyDataStore(_roleConfigurationOptions);

            _roleConfiguration.Roles.Returns(new List<Role> { new Role { Name = "role1", Version = "abc" }, new Role { Name = "role2", Version = "def" }, });

            await Assert.ThrowsAsync<KeyNotFoundException>(async () => await appSettingsSecurityDataStore.GetRoleAsync("role3", CancellationToken.None));
        }
    }
}
