// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterValidatorTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IFhirAuthorizationService _authorizationService = new DisabledFhirAuthorizationService();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();

        public SearchParameterValidatorTests()
        {
            _searchParameterDefinitionManager.When(s => s.GetSearchParameter(Arg.Is<Uri>(uri => uri != new Uri("http://duplicate")))).
                Do(x => throw new SearchParameterNotSupportedException("message"));
            _searchParameterDefinitionManager.GetSearchParameter(new Uri("http://duplicate")).Returns(new SearchParameterInfo("duplicate"));

            _fhirOperationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None).Returns(false);
        }

        [Theory]
        [MemberData(nameof(InvalidSearchParamData))]
        public async Task GivenInvalidSearchParam_WhenValidatingSearchParam_ThenResourceNotValidExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager);
            await Assert.ThrowsAsync<ResourceNotValidException>(() => validator.ValidateSearchParamterInput(searchParam, method, CancellationToken.None));
        }

        [Theory]
        [MemberData(nameof(ValidSearchParamData))]
        public async Task GivenValidSearchParam_WhenValidatingSearchParam_ThenNoExceptionThrown(SearchParameter searchParam, string method)
        {
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager);
            await validator.ValidateSearchParamterInput(searchParam, method, CancellationToken.None);
        }

        [Fact]
        public async Task GivenUnauthorizedUser_WhenValidatingSearchParam_ThenExceptionThrown()
        {
            var authorizationService = Substitute.For<IFhirAuthorizationService>();
            authorizationService.CheckAccess(Core.Features.Security.DataActions.Reindex).Returns(Core.Features.Security.DataActions.Write);
            var validator = new SearchParameterValidator(() => _fhirOperationDataStore.CreateMockScope(), authorizationService, _searchParameterDefinitionManager);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => validator.ValidateSearchParamterInput(new SearchParameter(), "POST", CancellationToken.None));
        }

        [Fact]
        public async Task GivenARunningReinxedJob_WhenValidatingSearchParam_ThenExceptionThrown()
        {
            var fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
            fhirOperationDataStore.CheckActiveReindexJobsAsync(Arg.Any<CancellationToken>()).Returns(true);

            var validator = new SearchParameterValidator(() => fhirOperationDataStore.CreateMockScope(), _authorizationService, _searchParameterDefinitionManager);

            await Assert.ThrowsAsync<JobConflictException>(() => validator.ValidateSearchParamterInput(new SearchParameter(), "POST", CancellationToken.None));
        }

        public static IEnumerable<object[]> InvalidSearchParamData()
        {
            var missingUrl = new SearchParameter();
            var duplicateUrl = new SearchParameter() { Url = "http://duplicate" };
            var uniqueUrl = new SearchParameter() { Url = "http://unique" };

            var data = new List<object[]>();
            data.Add(new object[] { missingUrl, "POST" });
            data.Add(new object[] { duplicateUrl, "POST" });
            data.Add(new object[] { uniqueUrl, "PUT" });
            data.Add(new object[] { uniqueUrl, "DELETE" });

            return data;
        }

        public static IEnumerable<object[]> ValidSearchParamData()
        {
            var duplicateUrl = new SearchParameter() { Url = "http://duplicate" };
            var uniqueUrl = new SearchParameter() { Url = "http://unique" };

            var data = new List<object[]>();
            data.Add(new object[] { uniqueUrl, "POST" });
            data.Add(new object[] { duplicateUrl, "PUT" });
            data.Add(new object[] { duplicateUrl, "DELETE" });

            return data;
        }
    }
}
