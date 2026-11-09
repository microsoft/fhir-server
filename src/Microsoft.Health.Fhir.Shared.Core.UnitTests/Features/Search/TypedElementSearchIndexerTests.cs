// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    [Trait(Traits.Category, Categories.Search)]
    public class TypedElementSearchIndexerTests
    {
        private readonly ISearchIndexer _searchIndexer;
        private readonly ISupportedSearchParameterDefinitionManager _definitionManager;

        private static readonly string ResourceStaus = "http://hl7.org/fhir/SearchParameter/Resource-status";
        private static readonly string ResourceUse = "http://hl7.org/fhir/SearchParameter/Resource-use";
        private static readonly string ResourceName = "http://hl7.org/fhir/SearchParameter/name";

        private const string CoverageStausExpression = "Coverage.status";
        private const string ObservationStausExpression = "Observation.status";
        private const string ClaimUseExpression = "Claim.use";
        private const string PatientNameExpression = "Patient.name";

        private static SearchParameterInfo statusSearchParameterInfo;

        public TypedElementSearchIndexerTests()
        {
            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            _definitionManager = supportedSearchParameterDefinitionManager;
            var typedElementToSearchValueConverterManager = GetTypeConverterAsync().Result;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            _searchIndexer = new TypedElementSearchIndexer(supportedSearchParameterDefinitionManager, typedElementToSearchValueConverterManager, referenceToElementResolver, modelInfoProvider, logger);

            List<string> baseResourceTypes = new List<string>() { "Resource" };
            List<string> targetResourceTypes = new List<string>() { "Coverage", "Observation", "Claim", "Patient" };
            statusSearchParameterInfo = new SearchParameterInfo("_status", "_status", (ValueSets.SearchParamType)SearchParamType.Token, new Uri(ResourceStaus), expression: CoverageStausExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes);
            var searchParameterInfos = new[]
            {
                statusSearchParameterInfo,
                new SearchParameterInfo("_status", "_status", (ValueSets.SearchParamType)SearchParamType.Token, new Uri(ResourceStaus), expression: ObservationStausExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
                new SearchParameterInfo("_use", "_use", (ValueSets.SearchParamType)SearchParamType.Token, new Uri(ResourceUse), expression: ClaimUseExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
                new SearchParameterInfo("name", "name", (ValueSets.SearchParamType)SearchParamType.String, new Uri(ResourceName), expression: PatientNameExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
            };
            supportedSearchParameterDefinitionManager.GetSearchParameters(Arg.Any<string>()).Returns(searchParameterInfos);
        }

        protected async Task<ITypedElementToSearchValueConverterManager> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            return fhirTypedElementToSearchValueConverterManager;
        }

        [Theory]
        [InlineData("Observation.value.where(matches('['))", ValueSets.SearchParamType.String)]
        [InlineData("Observation.value.where(matches('['))", ValueSets.SearchParamType.Composite)]
        [InlineData("Observation.value.where(matches($this & '['))", ValueSets.SearchParamType.String)]
        public void GivenExpressionEvaluationFailure_WhenExtract_ThenExceptionIdentifiesSearchParameter(string expression, ValueSets.SearchParamType searchParameterType)
        {
            // Arrange
            var url = new Uri("http://example.org/SearchParameter/evaluation-test");
            _definitionManager.GetSearchParameters("Observation").Returns(
                new[] { new SearchParameterInfo("evaluation-test", "evaluation-test", searchParameterType, url, expression: expression) });
            var observation = new Observation { Value = new FhirString("synthetic-value") };

            // Act
            var exception = Assert.Throws<SearchParameterEvaluationException>(() => _searchIndexer.Extract(observation.ToResourceElement()));

            // Assert
            Assert.Equal(url, exception.SearchParameterUrl);
            Assert.Equal(string.Format(Core.Resources.SearchParameterEvaluationFailed, url), exception.Message);
            Assert.NotNull(exception.InnerException);
            Assert.DoesNotContain("synthetic-value", exception.Message);
        }

        [Theory]
        [InlineData("Observation.value.where(")]
        [InlineData("Observation.value.nonExistingFunction()")]
        public void GivenImmediateExpressionFailure_WhenExtract_ThenOtherSearchParametersAreStillIndexed(string expression)
        {
            // Arrange
            var invalidParameter = new SearchParameterInfo(
                "invalid",
                "invalid",
                ValueSets.SearchParamType.String,
                new Uri("http://example.org/SearchParameter/invalid"),
                expression: expression);
            var validParameter = new SearchParameterInfo(
                "status",
                "status",
                ValueSets.SearchParamType.Token,
                new Uri(ResourceStaus),
                expression: ObservationStausExpression);
            _definitionManager.GetSearchParameters("Observation").Returns(new[] { invalidParameter, validParameter });
            var observation = new Observation { Status = ObservationStatus.Final };

            // Act
            var entries = _searchIndexer.Extract(observation.ToResourceElement());

            // Assert
            var entry = Assert.Single(entries);
            Assert.Equal("final", Assert.IsType<TokenSearchValue>(entry.Value).Code);
        }

        [Fact]
        public void GivenCompositeCompilationFailure_WhenExtract_ThenOriginalExceptionIsNotWrapped()
        {
            // Arrange
            _definitionManager.GetSearchParameters("Observation").Returns(
                new[]
                {
                    new SearchParameterInfo(
                        "invalid",
                        "invalid",
                        ValueSets.SearchParamType.Composite,
                        new Uri("http://example.org/SearchParameter/invalid"),
                        expression: "Observation.value.where("),
                });
            var observation = new Observation { Value = new FhirString("synthetic-value") };

            // Act
            var exception = Record.Exception(() => _searchIndexer.Extract(observation.ToResourceElement()));

            // Assert
            Assert.NotNull(exception);
            Assert.IsNotType<SearchParameterEvaluationException>(exception);
        }

        [Fact]
        public void GivenConverterFailure_WhenExtract_ThenOriginalExceptionIsNotWrapped()
        {
            // Arrange
            var converterManager = Substitute.For<ITypedElementToSearchValueConverterManager>();
            var converter = Substitute.For<ITypedElementToSearchValueConverter>();
            converterManager.TryGetConverter("string", typeof(StringSearchValue), out Arg.Any<ITypedElementToSearchValueConverter>())
                .Returns(call =>
                {
                    call[2] = converter;
                    return true;
                });
            var expectedException = new InvalidOperationException("Conversion failed.");
            converter.ConvertTo(Arg.Any<ITypedElement>()).Returns(_ => throw expectedException);
            _definitionManager.GetSearchParameters("Observation").Returns(
                new[]
                {
                    new SearchParameterInfo(
                        "value",
                        "value",
                        ValueSets.SearchParamType.String,
                        new Uri("http://example.org/SearchParameter/value"),
                        expression: "Observation.value"),
                });
            var indexer = new TypedElementSearchIndexer(
                _definitionManager,
                converterManager,
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                Substitute.For<ILogger<TypedElementSearchIndexer>>());
            var observation = new Observation { Value = new FhirString("synthetic-value") };

            // Act
            var exception = Assert.Throws<InvalidOperationException>(() => indexer.Extract(observation.ToResourceElement()));

            // Assert
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void GivenAValidResource_WhenExtract_ThenValidSearchIndexEntriesAreCreated()
        {
            var coverageResource = Samples.GetDefaultCoverage().ToPoco<Coverage>();

            var searchIndexEntry = _searchIndexer.Extract(coverageResource.ToResourceElement());
            Assert.NotEmpty(searchIndexEntry);

            var tokenSearchValue = searchIndexEntry.First().Value as TokenSearchValue;
            Assert.NotNull(tokenSearchValue);

            Assert.True(coverageResource.Status.Value.ToString().Equals(tokenSearchValue.Code, StringComparison.CurrentCultureIgnoreCase));
        }

        [Fact]
        public void GivenAValidResourceWithDuplicateSearchIndices_WhenExtract_ThenDistincSearchIndexEntriesAreCreated()
        {
            var patientResource = Samples.GetDefaultPatient().ToPoco<Patient>();
            var familyName = "Chalmers";
            var nameList = new List<HumanName>()
            {
                new HumanName() { Use = HumanName.NameUse.Official, Family = familyName },
                new HumanName() { Use = HumanName.NameUse.Official, Family = familyName },
            };
            patientResource.Name = nameList;

            var serachIndexEntry = _searchIndexer.Extract(patientResource.ToResourceElement());
            Assert.Single(serachIndexEntry);

            var nameSearchValue = serachIndexEntry.First().Value as StringSearchValue;
            Assert.Equal(familyName, nameSearchValue.String);
         }

#if !Stu3
        // For Stu3 - Coverage.status, Observation.status, and Claim.use are not required fields
        [Fact]
        public void GivenAnValidResource_WhenExtract_ThenExceptionIsNotThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithInvalidBundleEntry");

            foreach (var entry in new BundleWrapper(requestBundle.Instance).Entries)
            {
                ResourceElement resourceElement = null;
                string errorMessage = null;
                switch (entry.Resource.InstanceType)
                {
                    case "Coverage":
                        resourceElement = entry.Resource.ToPoco<Coverage>().ToResourceElement();
                        errorMessage = string.Format(Core.Resources.ValueCannotBeNull, CoverageStausExpression);
                        break;
                    case "Observation":
                        resourceElement = entry.Resource.ToPoco<Observation>().ToResourceElement();
                        errorMessage = string.Format(Core.Resources.ValueCannotBeNull, ObservationStausExpression);
                        break;
                    case "Claim":
                        resourceElement = entry.Resource.ToPoco<Claim>().ToResourceElement();
                        errorMessage = string.Format(Core.Resources.ValueCannotBeNull, ClaimUseExpression);
                        break;
                    default: break;
                }

                var exception = Record.Exception(() => _searchIndexer.Extract(resourceElement));
                Assert.Null(exception);
            }
        }
#endif
    }
}
