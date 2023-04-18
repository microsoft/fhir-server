// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterValidatorTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IAuthorizationService<DataActions> _authorizationService = new DisabledFhirAuthorizationService();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly IModelInfoProvider _modelInfoProvider = MockModelInfoProviderBuilder.Create(FhirSpecification.R4).AddKnownTypes("Patient").Build();

        public SearchParameterValidatorTests()
        {
            _searchParameterDefinitionManager.TryGetSearchParameter(Arg.Is<string>(uri => uri != "http://duplicate"), out _).Returns(false);
            _searchParameterDefinitionManager.TryGetSearchParameter("http://duplicate", out _).Returns(true);
            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", Arg.Is<string>(code => code != "duplicate"), out _).Returns(false);
            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", "duplicate", out _).Returns(true);
            _fhirOperationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None).Returns((false, string.Empty));
        }

        [Theory]
        [MemberData(nameof(InvalidSearchParamData))]
        public async Task GivenInvalidSearchParam_WhenValidatingSearchParam_ThenResourceNotValidExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider);
            await Assert.ThrowsAsync<ResourceNotValidException>(() => validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None));
        }

        [Theory]
        [MemberData(nameof(ValidSearchParamData))]
        public async Task GivenValidSearchParam_WhenValidatingSearchParam_ThenNoExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider);
            await validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None);
        }

        [Fact]
        public async Task GivenUnauthorizedUser_WhenValidatingSearchParam_ThenExceptionThrown()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.Write);
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), authorizationService, _searchParameterDefinitionManager, _modelInfoProvider);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => validator.ValidateSearchParameterInput(new SearchParameter(), "POST", CancellationToken.None));
        }

        [Fact]
        public async Task GivenARunningReinxedJob_WhenValidatingSearchParam_ThenExceptionThrown()
        {
            var fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
            fhirOperationDataStore.CheckActiveReindexJobsAsync(Arg.Any<CancellationToken>()).Returns((true, "id"));

            var validator = new SearchParameterValidator(() => fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider);

            await Assert.ThrowsAsync<JobConflictException>(() => validator.ValidateSearchParameterInput(new SearchParameter(), "POST", CancellationToken.None));
        }

        [Theory]
        [MemberData(nameof(DuplicateCodeAtBaseResourceData))]
        public async Task GivenInvalidSearchParamWithDuplicateCode_WhenValidatingSearchParam_ThenResourceNotValidExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager, _modelInfoProvider);
            await Assert.ThrowsAsync<ResourceNotValidException>(() => validator.ValidateSearchParameterInput(searchParam, method, CancellationToken.None));
        }

        public static IEnumerable<object[]> InvalidSearchParamData()
        {
            var missingUrl = new SearchParameter();
            var duplicateUrl = new SearchParameter { Url = "http://duplicate" };
            var brokenUrl = new SearchParameter { Url = "BrokenUrl" };
            var uniqueUrl = new SearchParameter { Url = "http://unique" };
            var duplicateCode = VersionSpecificSearchParameterFactory.CreateSearchParameter("http://unique", "duplicate", new[] { KnownResourceTypes.Patient });
            var nullCode = VersionSpecificSearchParameterFactory.CreateSearchParameter("http://unique", null, new[] { KnownResourceTypes.Patient });

            var data = new List<object[]>();
            data.Add(new object[] { missingUrl, "POST" });
            data.Add(new object[] { duplicateUrl, "POST" });
            data.Add(new object[] { brokenUrl, "POST" });
            data.Add(new object[] { uniqueUrl, "PUT" });
            data.Add(new object[] { uniqueUrl, "DELETE" });
            data.Add(new object[] { duplicateCode, "POST" });
            data.Add(new object[] { duplicateCode, "PUT" });
            data.Add(new object[] { nullCode, "POST" });

            return data;
        }

        public static IEnumerable<object[]> DuplicateCodeAtBaseResourceData()
        {
            var duplicateCode1 = VersionSpecificSearchParameterFactory.CreateSearchParameter("http://unique2", "duplicate", new[] { KnownResourceTypes.Resource });

            var data = new List<object[]>();
            data.Add(new object[] { duplicateCode1, "POST" });
            data.Add(new object[] { duplicateCode1, "PUT" });
            return data;
        }

        public static IEnumerable<object[]> ValidSearchParamData()
        {
            var duplicateUrl = new SearchParameter { Url = "http://duplicate" };
            var uniqueUrl = new SearchParameter { Url = "http://unique" };
            var uniqueCode = VersionSpecificSearchParameterFactory.CreateSearchParameter("http://unique", "unique", new[] { KnownResourceTypes.Patient });

            var data = new List<object[]>();
            data.Add(new object[] { uniqueUrl, "POST" });
            data.Add(new object[] { duplicateUrl, "PUT" });
            data.Add(new object[] { duplicateUrl, "DELETE" });
            data.Add(new object[] { uniqueCode, "POST" });

            return data;
        }
    }
}
