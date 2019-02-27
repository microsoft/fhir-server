// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using Microsoft.Health.ControlPlane.Core.Features.Exceptions;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
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
            _identityProvider = GetIdentityProvider("aad", "test", "https://microsoft.onmicrosoft.com/common/");

            _controlPlaneDataStore = Substitute.For<IControlPlaneDataStore>();
            _controlPlaneDataStore.GetIdentityProviderAsync(_identityProvider.Name, Arg.Any<CancellationToken>()).Returns(_identityProvider);
            _controlPlaneDataStore.UpsertIdentityProviderAsync(_identityProvider, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new UpsertResponse<IdentityProvider>(_identityProvider, UpsertOutcome.Updated, "testEtag"));

            IList<ResourcePermission> resourcePermissions = new List<ResourcePermission>();

            _role = GetRole("clinician", resourcePermissions);
            _controlPlaneDataStore.GetRoleAsync(_role.Name, Arg.Any<CancellationToken>()).Returns(_role);
            _controlPlaneDataStore.UpsertRoleAsync(_role, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new UpsertResponse<Role>(_role, UpsertOutcome.Updated, "testEtag"));
            _controlPlaneDataStore.DeleteRoleAsync(_role.Name, Arg.Any<string>(), Arg.Any<CancellationToken>());

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
            var role = await _rbacService.UpsertRoleAsync(_role, "someETag", CancellationToken.None);

            Assert.Same(_role, role.Resource);
        }

        [Fact]
        public async void GivenAnIdentityProvider_WhenUpsertingIdentityProvider_ThenDataStoreIsCalled()
        {
            var identityProviderResponse = await _rbacService.UpsertIdentityProviderAsync(_identityProvider, "someETag", CancellationToken.None);

            Assert.Same(_identityProvider, identityProviderResponse.Resource);
        }

        [Theory]
        [InlineData("aad", "fhir-api", "http://testauthority")]
        [InlineData("test1", "fhir-api-1", "http://testauthority1")]
        [InlineData("test2", "fhir-api-2", "http://testauthority2")]
        public async void GivenAName_WhenGettingIdentityProvider_ThenIdentityProviderReturned(string name, string audience, string authority)
        {
            var identityProvider = GetIdentityProvider(name, audience, authority);
            _controlPlaneDataStore.GetIdentityProviderAsync(identityProvider.Name, Arg.Any<CancellationToken>()).Returns(identityProvider);

            var retIdentityProvider = await _rbacService.GetIdentityProviderAsync(identityProvider.Name, CancellationToken.None);

            VerifyIdentityProvider(name, audience, authority, "1.0", identityProvider);
        }

        private static void VerifyIdentityProvider(string name, string audience, string authority, string version, IdentityProvider identityProvider)
        {
            Assert.Equal(name, identityProvider.Name);
            Assert.Equal(audience, identityProvider.Audience[0]);
            Assert.Equal(authority, identityProvider.Authority);
        }

        [Fact]
        public async void GivenTheDataStore_WhenGettingIAlldentityProviders_ThenAllIdentityProvidersReturned()
        {
            var idpDict = new Dictionary<string, IdentityProvider>();
            idpDict.Add("aad", GetIdentityProvider("aad", "fhir-api", "http://testauthority"));
            idpDict.Add("test1", GetIdentityProvider("test1", "fhir-api-1", "http://testauthority1"));
            idpDict.Add("test2", GetIdentityProvider("test2", "fhir-api-2", "http://testauthority2"));

            _controlPlaneDataStore.GetAllIdentityProvidersAsync(Arg.Any<CancellationToken>()).Returns(idpDict.Values);

            var identityProviders = await _rbacService.GetAllIdentityProvidersAsync(CancellationToken.None);
            Assert.Equal(3, identityProviders.Count());

            VerifyIdentityProvider("aad", "fhir-api", "http://testauthority", "1.0", idpDict["aad"]);
            VerifyIdentityProvider("test1", "fhir-api-1", "http://testauthority1", "1.0", idpDict["test1"]);
            VerifyIdentityProvider("test2", "fhir-api-2", "http://testauthority2", "1.0", idpDict["test2"]);
        }

        [Fact]
        public async void GivenAnIdentityProvider_WhenUpsertOnNonExisting_ThenIdentityProviderIsCreated()
        {
            var identityProviderToCreate = GetIdentityProvider("testnew", "audnew", "http://authnew");
            var upsertResponse = new UpsertResponse<IdentityProvider>(identityProviderToCreate, UpsertOutcome.Created, "someEtag");

            _controlPlaneDataStore.UpsertIdentityProviderAsync(identityProviderToCreate, null, CancellationToken.None).Returns(upsertResponse);

            UpsertResponse<IdentityProvider> retUpsertResponse = await _rbacService.UpsertIdentityProviderAsync(identityProviderToCreate, null, CancellationToken.None);

            Assert.Equal(UpsertOutcome.Created, upsertResponse.OutcomeType);
            VerifyIdentityProvider("testnew", "audnew", "http://authnew", "1.0", upsertResponse.Resource);
        }

        [Fact]
        public async void GivenAnIdentityProvider_WhenUpsertOnExisting_ThenIdentityProviderIsUpdated()
        {
            var identityProviderToUpdate = GetIdentityProvider("testupd", "audupd", "http://authupd");

            var upsertResponse = new UpsertResponse<IdentityProvider>(identityProviderToUpdate, UpsertOutcome.Updated, "someEtag");

            _controlPlaneDataStore.UpsertIdentityProviderAsync(identityProviderToUpdate, null, CancellationToken.None).Returns(upsertResponse);

            UpsertResponse<IdentityProvider> retUpsertResponse = await _rbacService.UpsertIdentityProviderAsync(identityProviderToUpdate, null, CancellationToken.None);

            Assert.Equal(UpsertOutcome.Updated, upsertResponse.OutcomeType);
            VerifyIdentityProvider("testupd", "audupd", "http://authupd", "1.0", upsertResponse.Resource);
        }

        [Theory]
        [InlineData(null, "aadasd", "aud")]
        [InlineData("asdadas", null, "aud")]
        [InlineData("adsadsa", "aadasd", null)]
        [InlineData(null, null, "aud")]
        public async void GivenAnIdentityProvider_WhenUpsertWithValidationFailure_ThenInvalidDefintionExceptionIsThrown(string name, string authority, string audience)
        {
            var identityProviderToUpdate = Substitute.ForPartsOf<IdentityProvider>();
            identityProviderToUpdate.Name.Returns(name);
            identityProviderToUpdate.Authority.Returns(authority);
            identityProviderToUpdate.Audience.Returns(new List<string> { audience });

            identityProviderToUpdate.ValidateAuthority().Returns(Enumerable.Empty<ValidationResult>());

            var upsertResponse = new UpsertResponse<IdentityProvider>(identityProviderToUpdate, UpsertOutcome.Updated, "someEtag");

            _controlPlaneDataStore.UpsertIdentityProviderAsync(identityProviderToUpdate, null, CancellationToken.None).Returns(upsertResponse);
            var exception = await Assert.ThrowsAsync<InvalidDefinitionException>(() => _rbacService.UpsertIdentityProviderAsync(identityProviderToUpdate, null, CancellationToken.None));

            Assert.True(exception.Issues.Count() > 0);
        }

        private static IdentityProvider GetIdentityProvider(string name, string audience, string authority)
        {
            var identityProvider = Substitute.For<IdentityProvider>();
            identityProvider.Name.Returns(name);
            identityProvider.Authority.Returns(authority);
            identityProvider.Audience.Returns(new List<string> { audience });

            identityProvider.Validate(Arg.Any<ValidationContext>()).Returns(Enumerable.Empty<ValidationResult>());

            return identityProvider;
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
