// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterToTypeResolverTests
    {
        private static readonly FhirPathCompiler _compiler = new FhirPathCompiler();

        [Fact]
        public void GivenAFhirPathExpressionWithFirstFunction_WhenResolvingTypes_ThenTheyAreReturnedCorrectly()
        {
            var expression = _compiler.Parse("Patient.extension.where(url = 'http://hl7.org/fhir/us/core/StructureDefinition/us-core-race').first().extension.where(url = 'ombCategory').value");
            SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
                KnownResourceTypes.Patient,
                (SearchParamType.Token, expression, new Uri("http://hl7.org/fhir/SearchParameter/Patient-race")),
                null).ToArray();
            var types = results.Select(x => x.FhirNodeType).OrderBy(x => x).ToArray();

            if (ModelInfoProvider.Version == FhirSpecification.Stu3)
            {
                Assert.Equal(51, types.Length);
            }
            else if (ModelInfoProvider.Version == FhirSpecification.R4)
            {
                Assert.Equal(59, types.Length);
            }
            else
            {
                Assert.Equal(62, types.Length);
            }
        }

        [Fact]
        public void GivenAFhirPathExpressionWithTwoPossibleOutcomeTypes_WhenResolve_TwoTypesReturned()
        {
            var expression = _compiler.Parse("CarePlan.activity.detail.product");
            SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
                "CarePlan",
                (SearchParamType.Reference, expression, new Uri("http://hl7.org/fhir/SearchParameter/Patient-race")),
                null).ToArray();

            Assert.Equal(2, results.Length);
        }

        [Fact]
        public void GivenAFhirPathExpressionWithAsFunction_WhenResolvingTypes_ThenTheyAreReturnedCorrectly()
        {
            var expression = _compiler.Parse("Patient.extension.where(url = 'http://hl7.org/fhir/us/core/StructureDefinition/us-core-race').first().extension.where(url = 'ombCategory').value.as(string)");
            SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
                KnownResourceTypes.Patient,
                (SearchParamType.Token, expression, new Uri("http://hl7.org/fhir/SearchParameter/Patient-race")),
                null).ToArray();

            Assert.Equal("string", results.Single().FhirNodeType);
        }
    }
}
