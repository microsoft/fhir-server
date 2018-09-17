// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbStorageTests : FhirStorageTestsBase, IClassFixture<CosmosAdminDataStore>
    {
        private readonly CosmosAdminDataStore _dataStore;

        public CosmosDbStorageTests(CosmosAdminDataStore dataStore)
            : base(dataStore, dataStore)
        {
            _dataStore = dataStore;
        }

        [Fact]
        public async Task GivenAContinuationToken_WhenGettingAnId_ThenTheOriginalTokenIsHashed()
        {
            var ct = Guid.NewGuid().ToString();

            var id = await _dataStore.SaveContinuationTokenAsync(ct);

            var result = await _dataStore.GetContinuationTokenAsync(id);

            Assert.Equal(ct, result);
        }

        [Fact]
        public async Task GivenAnEmptyContinuationToken_WhenGettingAnId_ThenAnErrorShouldBeThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _dataStore.SaveContinuationTokenAsync(null));
        }

        [Fact]
        public async Task GivenACorruptContinuationTokenId_WhenGettingAContinuationToken_ThenAnErrorIsThrown()
        {
            var corrupt = Guid.NewGuid().ToString();

            await Assert.ThrowsAnyAsync<InvalidSearchOperationException>(async () => await _dataStore.GetContinuationTokenAsync(corrupt));
        }

        [Fact]
        public async Task GivenANewResource_WhenUpserting_ThenTheVersionStartsAt1()
        {
            var saveResult = await FhirRepository.UpsertAsync(Samples.GetJsonSample("Weight"));

            Assert.Equal("1", saveResult.Resource.Meta.VersionId);

            saveResult = await FhirRepository.UpsertAsync(saveResult.Resource);

            Assert.Equal("2", saveResult.Resource.Meta.VersionId);
        }

        [Fact]
        public async Task GivenMultipleRoles_WhenRetrievingAll_ThenNewRolesAreReturned()
        {
            var role1 = new Role { Name = Guid.NewGuid().ToString() };
            var role2 = new Role { Name = Guid.NewGuid().ToString() };
            var saveResult1 = await SecurityRepository.UpsertRoleAsync(role1, null, CancellationToken.None);
            var saveResult2 = await SecurityRepository.UpsertRoleAsync(role2, null, CancellationToken.None);

            var resultSet = await SecurityRepository.GetAllRolesAsync(CancellationToken.None);

            Assert.Single(resultSet, role => role.Name == saveResult1.Name && role.Version == saveResult1.Version);
            Assert.Single(resultSet, role => role.Name == saveResult2.Name && role.Version == saveResult2.Version);
        }

        [Fact]
        public async Task GivenRoles_WhenRetrievingSingleRole_ThenRoleReturned()
        {
            var role = new Role { Name = Guid.NewGuid().ToString() };
            var saveResult = await SecurityRepository.UpsertRoleAsync(role, null, CancellationToken.None);

            var result = await SecurityRepository.GetRoleAsync(saveResult.Name, CancellationToken.None);

            Assert.Equal(saveResult.Name, result.Name);
            Assert.Equal(saveResult.Version, result.Version);
        }

        [Fact]
        public async Task GivenRoles_WhenRetrievingNonExistentRole_ThenAnErrorIsThrown()
        {
            var role = new Role { Name = Guid.NewGuid().ToString() };
            await SecurityRepository.UpsertRoleAsync(role, null, CancellationToken.None);

            await Assert.ThrowsAnyAsync<InvalidSearchOperationException>(async () => await SecurityRepository.GetRoleAsync(Guid.NewGuid().ToString(), CancellationToken.None));
        }

        [Fact]
        public async Task GivenRoles_WhenGettingAfterDeleting_ThenAnErrorIsThrown()
        {
            var role = new Role { Name = Guid.NewGuid().ToString() };
            await SecurityRepository.UpsertRoleAsync(role, null, CancellationToken.None);
            await SecurityRepository.GetRoleAsync(role.Name, CancellationToken.None);
            await SecurityRepository.DeleteRoleAsync(role.Name, CancellationToken.None);

            await Assert.ThrowsAnyAsync<InvalidSearchOperationException>(async () => await SecurityRepository.GetRoleAsync(role.Name, CancellationToken.None));
        }

        [Fact]
        public async Task GivenARole_WhenUpserting_ThenVersionIsSet()
        {
            var roleName = Guid.NewGuid().ToString();
            var role = new Role { Name = roleName };
            var saveResult = await SecurityRepository.UpsertRoleAsync(role, null, CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(saveResult.Version));
            Assert.Equal(roleName, saveResult.Name);
        }

        [Fact]
        public async Task GivenARole_WhenUpserting_ThenVersionIsUpdated()
        {
            var roleName = Guid.NewGuid().ToString();
            var version = Guid.NewGuid().ToString();
            var role = new Role { Name = roleName, Version = version };
            var saveResult = await SecurityRepository.UpsertRoleAsync(role, null, CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(saveResult.Version));
            Assert.Equal(roleName, saveResult.Name);
            Assert.NotEqual(version, saveResult.Version);
        }

        [Fact]
        public async Task GivenARoleAndCorrectWeakETag_WhenUpserting_ThenUpdateSucceeds()
        {
            var roleName = Guid.NewGuid().ToString();
            var role = new Role { Name = roleName };
            var firstResult = await SecurityRepository.UpsertRoleAsync(role, null, CancellationToken.None);

            var secondResult = await SecurityRepository.UpsertRoleAsync(firstResult, WeakETag.FromVersionId(firstResult.Version), CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(secondResult.Version));
            Assert.Equal(roleName, secondResult.Name);
            Assert.NotEqual(firstResult.Version, secondResult.Version);
        }

        [Fact]
        public async Task GivenARoleAndIncorrectWeakETag_WhenUpserting_ThenAnErrorIsThrown()
        {
            var roleName = Guid.NewGuid().ToString();
            var role = new Role { Name = roleName };
            var firstResult = await SecurityRepository.UpsertRoleAsync(role, null, CancellationToken.None);

            await Assert.ThrowsAnyAsync<ResourceConflictException>(async () => await SecurityRepository.UpsertRoleAsync(firstResult, WeakETag.FromVersionId("not-a-version"), CancellationToken.None));
        }
    }
}
