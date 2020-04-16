// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterSupportResolverTests : IClassFixture<SearchParameterFixtureData>
    {
        private readonly ITestOutputHelper _outputHelper;
        private readonly SearchParameterFixtureData _fixtureData;
        private SearchParameterSupportResolver _resolver;

        public SearchParameterSupportResolverTests(ITestOutputHelper outputHelper, SearchParameterFixtureData fixtureData)
        {
            _outputHelper = outputHelper;
            _fixtureData = fixtureData;

            _resolver = new SearchParameterSupportResolver(_fixtureData.SearchDefinitionManager, SearchParameterFixtureData.Manager);
        }

        [Fact]
        public void GivenASupportedSearchParameter_WhenResolvingSupport_ThenTrueIsReturned()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                SearchParamType.Quantity,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
                expression: "Condition.abatement.as(Range)",
                baseResourceTypes: new[] { "Condition" });

            bool supported = _resolver.IsSearchParameterSupported(sp);

            Assert.True(supported);
        }

        [Fact]
        public void GivenAnUnsupportedSearchParameter_WhenResolvingSupport_ThenFalseIsReturned()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                SearchParamType.Uri,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
                expression: "Condition.abatement.as(Range)",
                baseResourceTypes: new[] { "Condition" });

            // Can not convert Range => Uri
            bool supported = _resolver.IsSearchParameterSupported(sp);

            Assert.False(supported);
        }

        [Fact]
        public void GivenAPartiallySupportedSearchParameter_WhenResolvingSupport_ThenFalseIsReturned()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                SearchParamType.Quantity,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
                expression: "Condition.asserter | Condition.abatement.as(Range)",
                baseResourceTypes: new[] { "Condition" });

            // Condition.asserter cannot be translated to Quantity
            // Condition.abatement.as(Range) CAN be translated to Quantity
            bool supported = _resolver.IsSearchParameterSupported(sp);

            Assert.False(supported);
        }
    }
}
