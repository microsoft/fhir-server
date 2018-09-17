// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Security;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
    public class SecurityRepositoryTests
    {
        private readonly ISecurityDataStore _dataStore;
        private readonly SecurityRepository _repository;

        public SecurityRepositoryTests()
        {
            _dataStore = Substitute.For<ISecurityDataStore>();

            _repository = new SecurityRepository(_dataStore);
        }

        [Fact]
        public async Task GivenASecurityRepository_WhenGettingARoleWithNullRoleName_ThenArgumentNullExceptionThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.GetRoleAsync(null, CancellationToken.None));
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public async Task GivenASecurityRepository_WhenGettingARoleWithInvalideRoleName_ThenArgumentExceptionThrown(string roleName)
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await _repository.GetRoleAsync(roleName, CancellationToken.None));
        }

        [Fact]
        public async Task GivenASecurityRepository_WhenUpsertingRoleWithNull_ThenArgumentNullExceptionThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.UpsertRoleAsync(null, null, CancellationToken.None));
        }

        [Fact]
        public async Task GivenASecurityRepository_WhenDeletingARoleWithNullRoleName_ThenArgumentNullExceptionThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _repository.DeleteRoleAsync(null, CancellationToken.None));
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public async Task GivenASecurityRepository_WhenDeletingARoleWithInvalideRoleName_ThenArgumentExceptionThrown(string roleName)
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await _repository.DeleteRoleAsync(roleName, CancellationToken.None));
        }
    }
}
