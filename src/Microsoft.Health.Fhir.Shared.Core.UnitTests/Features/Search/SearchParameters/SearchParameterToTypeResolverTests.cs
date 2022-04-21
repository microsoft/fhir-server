// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.Category, Categories.Search)]
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
                Assert.Equal(61, types.Length);
            }
        }

        [Fact]
        public void GivenAFhirPathExpressionWithTwoPossibleOutcomeTypes_WhenResolve_TwoTypesReturned()
        {
            var path = (ModelInfoProvider.Version == FhirSpecification.R5) ?
                "CarePlan.activity.plannedActivityDetail.product" :
                "CarePlan.activity.detail.product";
            var expression = _compiler.Parse(path);
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

        [Fact]
        public void GivenAFhirPathExpressionAsConstructorFilter_WhenResolvingTypes_ThenTheyReturnedSameAsWhen()
        {
            var whereExpression = _compiler.Parse("Patient.extension.where(url = 'http://hl7.org/fhir/us/core/StructureDefinition/us-core-race')");
            var constructorExpression = _compiler.Parse("Patient.extension(url = 'http://hl7.org/fhir/us/core/StructureDefinition/us-core-race')");
            var whereResult = SearchParameterToTypeResolver.Resolve(
               KnownResourceTypes.Patient,
               (SearchParamType.Token, whereExpression, new Uri("http://hl7.org/fhir/SearchParameter/Patient-race")),
               null).First();

            var constuctorResult = SearchParameterToTypeResolver.Resolve(
                KnownResourceTypes.Patient,
                (SearchParamType.Token, constructorExpression, new Uri("http://hl7.org/fhir/SearchParameter/Patient-race")),
                null).First();
            Assert.Equal(whereResult.ClassMapping, constuctorResult.ClassMapping);
            Assert.Equal(whereResult.Path, constuctorResult.Path);
            Assert.Equal(whereResult.SearchParamType, constuctorResult.SearchParamType);
            Assert.Equal(whereResult.FhirNodeType, constuctorResult.FhirNodeType);
            Assert.Equal(whereResult.Definition, constuctorResult.Definition);
        }

        [Fact]
        public void GivenAFhirPathExpressionWithOfType_WhenResolvingTypes_ThenTheyAreReturnedCorrectly()
        {
            var expression = _compiler.Parse("QuestionnaireResponse.item.where(extension('http://hl7.org/fhir/StructureDefinition/questionnaireresponse-isSubject').exists()).answer.value.ofType(Reference)");
            SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
    KnownResourceTypes.QuestionnaireResponse,
    (SearchParamType.Reference, expression, new Uri("http://hl7.org/fhir/StructureDefinition/questionnaireresponse-isSubject")),
    null).ToArray();

            Assert.Equal("Reference", results.Single().FhirNodeType);
        }

        [Fact]
        public void GivenABadFhirPathExpression_WhenResolving_ThenResolveThrowException()
        {
            var expression = _compiler.Parse("Patient.cookie");
            Assert.Throws<NotSupportedException>(() =>
                SearchParameterToTypeResolver.Resolve(
                    KnownResourceTypes.Patient,
                    (SearchParamType.Token, expression, new Uri("http://hl7.org/fhir/SearchParameter/Patient-cookie")),
                    null).ToArray());
        }
    }
}
