// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [CollectionDefinition(Categories.IndexAndReindex, DisableParallelization = true)]
    [Collection(Categories.IndexAndReindex)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CustomSearchParamTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const int MaxAllowedUrlLength = 128;
        private const int MaxRetryCount = 10;
        private const string UrlLengthValidationMessage = "exceeds the maximum length limit of 128";

        public CustomSearchParamTests(HttpIntegrationTestFixture fixture, ITestOutputHelper output)
            : base(fixture)
        {
            _fixture = fixture;
            _output = output;
        }

        [RetryTheory]
        [InlineData("SearchParameterBadSyntax", "The search parameter definition contains one or more invalid entries.")]
#if Stu3 || R4 || R4B
        [InlineData("SearchParameterInvalidBase", "Literal 'foo' is not a valid value for enumeration 'ResourceType'")]
#else
        [InlineData("SearchParameterInvalidBase", "Literal 'foo' is not a valid value for enumeration 'VersionIndependentResourceTypesAll'")]
#endif
        [InlineData("SearchParameterExpressionWrongProperty", "Can't find 'Encounter.diagnosis.foo' in type 'Encounter'")]
        [InlineData("SearchParameterInvalidType", "Literal 'foo' is not a valid value for enumeration 'SearchParamType'")]
        [InlineData("SearchParameterMissingBase", "cardinality is 1")]
        [InlineData("SearchParameterMissingExpression", "not supported")]
        [InlineData("SearchParameterMissingType", "cardinality 1 cannot be null")]
        [InlineData("SearchParameterUnsupportedType", "not supported")]
        public async Task GivenAnInvalidSearchParam_WhenCreatingParam_ThenMeaningfulErrorReturned(string searchParamFile, string errorMessage)
        {
            var searchParam = Samples.GetJson(searchParamFile);

            try
            {
                await Client.PostAsync("SearchParameter", searchParam);
            }
            catch (FhirClientException ex)
            {
                Assert.Contains(ex.OperationOutcome.Issue, i => i.Diagnostics.Contains(errorMessage));
            }
        }

        [Fact]
        public async Task GivenASearchParameterWithUrlLongerThan128_WhenCreating_ThenValidationErrorReturned()
        {
            SearchParameter searchParam = CreateCustomSearchParameter(MaxAllowedUrlLength + 1);

            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(() => Client.CreateAsync(searchParam));

            Assert.Contains(exception.OperationOutcome.Issue, issue => issue.Diagnostics.Contains(UrlLengthValidationMessage));
        }

        [Fact]
        public async Task GivenAnExistingSearchParameter_WhenUpdatingWithUrlLongerThan128_ThenValidationErrorReturned()
        {
            SearchParameter searchParam = CreateCustomSearchParameter();

            using FhirResponse<SearchParameter> createResponse = await Client.UpdateAsync(searchParam);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            searchParam.Url = CreateCustomSearchParameter(MaxAllowedUrlLength + 1).Url;

            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(() => Client.UpdateAsync(searchParam));

            Assert.Contains(exception.OperationOutcome.Issue, issue => issue.Diagnostics.Contains(UrlLengthValidationMessage));
        }

        private static SearchParameter CreateCustomSearchParameter(int urlLength = MaxAllowedUrlLength)
        {
            string suffix = Guid.NewGuid().ToString("N");
            const string prefix = "http://example.org/fhir/SearchParameter/";

#if R5
            var baseResourceTypes = new List<VersionIndependentResourceTypesAll?>
            {
                VersionIndependentResourceTypesAll.Patient,
            };
#else
            var baseResourceTypes = new List<ResourceType?>
            {
                ResourceType.Patient,
            };
#endif

            return new SearchParameter
            {
                Id = $"custom-search-param-{suffix[..8]}",
                Url = prefix + new string('a', urlLength - prefix.Length),
                Name = $"CustomSearchParam{suffix[..8]}",
                Status = PublicationStatus.Draft,
                Description = new Markdown("Custom search parameter used for E2E URL validation tests."),
                Code = $"customparam{suffix[..8]}",
                Base = baseResourceTypes,
                Type = SearchParamType.String,
                Expression = "Patient.name",
            };
        }
    }
}
