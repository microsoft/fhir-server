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
        private readonly ITypedElementToSearchValueConverterManager _typedElementToSearchValueConverterManager;

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
            _typedElementToSearchValueConverterManager = GetTypeConverterAsync().Result;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            _searchIndexer = new TypedElementSearchIndexer(supportedSearchParameterDefinitionManager, _typedElementToSearchValueConverterManager, referenceToElementResolver, modelInfoProvider, logger);

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

        [Fact]
        public void GivenResourceWithQuantitySearchParameter_WhenExtract_ThenQuantitySearchValuesAreCreated()
        {
            // Test extraction of Quantity search parameters
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Value = new Quantity
            {
                Value = 85.5m,
                Unit = "kg",
                System = "http://unitsofmeasure.org",
                Code = "kg",
            };

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var quantitySearchParam = new SearchParameterInfo(
                "value-quantity",
                "value-quantity",
                ValueSets.SearchParamType.Quantity,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-quantity"),
                expression: "Observation.value.ofType(Quantity)");

            supportedSearchParameterDefinitionManager.GetSearchParameters("Observation")
                .Returns(new[] { quantitySearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(observation.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var quantityEntry = searchIndexEntries.FirstOrDefault(e => e.SearchParameter.Code == "value-quantity");
            Assert.NotNull(quantityEntry);

            var quantityValue = quantityEntry.Value as QuantitySearchValue;
            Assert.NotNull(quantityValue);
            Assert.Equal(85.5m, quantityValue.Low);
            Assert.Equal("http://unitsofmeasure.org", quantityValue.System);
            Assert.Equal("kg", quantityValue.Code);
        }

        [Fact]
        public void GivenResourceWithDateSearchParameter_WhenExtract_ThenDateTimeSearchValuesAreCreated()
        {
            // Test extraction of Date search parameters
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.BirthDate = "1974-12-25";

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var birthDateSearchParam = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                ValueSets.SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                expression: "Patient.birthDate");

            supportedSearchParameterDefinitionManager.GetSearchParameters("Patient")
                .Returns(new[] { birthDateSearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(patient.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var dateEntry = searchIndexEntries.FirstOrDefault(e => e.SearchParameter.Code == "birthdate");
            Assert.NotNull(dateEntry);

            var dateValue = dateEntry.Value as DateTimeSearchValue;
            Assert.NotNull(dateValue);
            Assert.Equal(1974, dateValue.Start.Year);
            Assert.Equal(12, dateValue.Start.Month);
            Assert.Equal(25, dateValue.Start.Day);
        }

        [Fact]
        public void GivenResourceWithReferenceSearchParameter_WhenExtract_ThenReferenceSearchValuesAreCreated()
        {
            // Test extraction of Reference search parameters
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Subject = new ResourceReference("Patient/123");

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var subjectSearchParam = new SearchParameterInfo(
                "subject",
                "subject",
                ValueSets.SearchParamType.Reference,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
                expression: "Observation.subject",
                targetResourceTypes: new List<string> { "Patient" });

            supportedSearchParameterDefinitionManager.GetSearchParameters("Observation")
                .Returns(new[] { subjectSearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(observation.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var referenceEntry = searchIndexEntries.FirstOrDefault(e => e.SearchParameter.Code == "subject");
            Assert.NotNull(referenceEntry);

            var referenceValue = referenceEntry.Value as ReferenceSearchValue;
            Assert.NotNull(referenceValue);
            Assert.Equal("Patient", referenceValue.ResourceType);
            Assert.Equal("123", referenceValue.ResourceId);
        }

        [Fact]
        public void GivenResourceWithNumberSearchParameter_WhenExtract_ThenNumberSearchValuesAreCreated()
        {
            // Test extraction of Number search parameters
            var riskAssessment = new RiskAssessment
            {
                Id = "test-risk",
                Status = ObservationStatus.Final,
                Subject = new ResourceReference("Patient/123"),
                Prediction = new List<RiskAssessment.PredictionComponent>
                {
                    new RiskAssessment.PredictionComponent
                    {
                        Probability = new FhirDecimal(0.85m),
                    },
                },
            };

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var probabilitySearchParam = new SearchParameterInfo(
                "probability",
                "probability",
                ValueSets.SearchParamType.Number,
                new Uri("http://hl7.org/fhir/SearchParameter/RiskAssessment-probability"),
                expression: "RiskAssessment.prediction.probability");

            supportedSearchParameterDefinitionManager.GetSearchParameters("RiskAssessment")
                .Returns(new[] { probabilitySearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(riskAssessment.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var numberEntry = searchIndexEntries.FirstOrDefault(e => e.SearchParameter.Code == "probability");
            Assert.NotNull(numberEntry);

            var numberValue = numberEntry.Value as NumberSearchValue;
            Assert.NotNull(numberValue);
            Assert.Equal(0.85m, numberValue.Low);
        }

        [Fact]
        public void GivenResourceWithUriSearchParameter_WhenExtract_ThenUriSearchValuesAreCreated()
        {
            // Test extraction of Uri search parameters
            var valueSet = new ValueSet
            {
                Id = "test-valueset",
                Url = "http://example.org/fhir/ValueSet/test",
                Status = PublicationStatus.Active,
            };

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var urlSearchParam = new SearchParameterInfo(
                "url",
                "url",
                ValueSets.SearchParamType.Uri,
                new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"),
                expression: "ValueSet.url");

            supportedSearchParameterDefinitionManager.GetSearchParameters("ValueSet")
                .Returns(new[] { urlSearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(valueSet.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var uriEntry = searchIndexEntries.FirstOrDefault(e => e.SearchParameter.Code == "url");
            Assert.NotNull(uriEntry);

            var uriValue = uriEntry.Value as UriSearchValue;
            Assert.NotNull(uriValue);
            Assert.Equal("http://example.org/fhir/ValueSet/test", uriValue.Uri);
        }

        [Fact]
        public void GivenResourceWithMultipleValuesForSearchParameter_WhenExtract_ThenMultipleSearchValuesAreCreated()
        {
            // Test extraction when a search parameter extracts multiple values
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.Identifier = new List<Identifier>
            {
                new Identifier("http://hospital.org/mrn", "12345"),
                new Identifier("http://national-id.org", "987654"),
                new Identifier("http://insurance.org", "INS-001"),
            };

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var identifierSearchParam = new SearchParameterInfo(
                "identifier",
                "identifier",
                ValueSets.SearchParamType.Token,
                new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"),
                expression: "Patient.identifier");

            supportedSearchParameterDefinitionManager.GetSearchParameters("Patient")
                .Returns(new[] { identifierSearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(patient.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var identifierEntries = searchIndexEntries.Where(e => e.SearchParameter.Code == "identifier").ToList();
            Assert.Equal(3, identifierEntries.Count);

            var tokenValues = identifierEntries.Select(e => e.Value as TokenSearchValue).ToList();
            Assert.All(tokenValues, tv => Assert.NotNull(tv));
            Assert.Contains(tokenValues, tv => tv.System == "http://hospital.org/mrn" && tv.Code == "12345");
            Assert.Contains(tokenValues, tv => tv.System == "http://national-id.org" && tv.Code == "987654");
            Assert.Contains(tokenValues, tv => tv.System == "http://insurance.org" && tv.Code == "INS-001");
        }

        [Fact]
        public void GivenResourceWithEmptySearchParameterExpression_WhenExtract_ThenNoSearchValuesAreCreated()
        {
            // Test edge case: search parameter with expression that returns no values
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.Deceased = null; // No deceased value

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var deceasedSearchParam = new SearchParameterInfo(
                "deceased",
                "deceased",
                ValueSets.SearchParamType.Token,
                new Uri("http://hl7.org/fhir/SearchParameter/Patient-deceased"),
                expression: "Patient.deceased");

            supportedSearchParameterDefinitionManager.GetSearchParameters("Patient")
                .Returns(new[] { deceasedSearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(patient.ToResourceElement());

            // Should not throw, but should not create any entries for deceased parameter
            var deceasedEntries = searchIndexEntries.Where(e => e.SearchParameter.Code == "deceased").ToList();
            Assert.Empty(deceasedEntries);
        }

        [Fact]
        public void GivenResourceWithChoiceTypeSearchParameter_WhenExtract_ThenCorrectTypeIsExtracted()
        {
            // Test extraction from choice type elements (e.g., value[x])
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Value = new FhirString("test-string-value");

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var valueStringSearchParam = new SearchParameterInfo(
                "value-string",
                "value-string",
                ValueSets.SearchParamType.String,
                new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-string"),
                expression: "Observation.value.ofType(string)");

            supportedSearchParameterDefinitionManager.GetSearchParameters("Observation")
                .Returns(new[] { valueStringSearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(observation.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var stringEntry = searchIndexEntries.FirstOrDefault(e => e.SearchParameter.Code == "value-string");
            Assert.NotNull(stringEntry);

            var stringValue = stringEntry.Value as StringSearchValue;
            Assert.NotNull(stringValue);
            Assert.Equal("test-string-value", stringValue.String);
        }

        [Fact]
        public void GivenResourceWithComplexFhirPathExpression_WhenExtract_ThenValuesAreCorrectlyExtracted()
        {
            // Test extraction with complex FhirPath expressions
            var patient = Samples.GetDefaultPatient().ToPoco<Patient>();
            patient.Name = new List<HumanName>
            {
                new HumanName
                {
                    Use = HumanName.NameUse.Official,
                    Family = "Smith",
                    Given = new[] { "John", "James" },
                },
            };

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            var typedElementToSearchValueConverterManager = _typedElementToSearchValueConverterManager;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var givenSearchParam = new SearchParameterInfo(
                "given",
                "given",
                ValueSets.SearchParamType.String,
                new Uri("http://hl7.org/fhir/SearchParameter/Patient-given"),
                expression: "Patient.name.given");

            supportedSearchParameterDefinitionManager.GetSearchParameters("Patient")
                .Returns(new[] { givenSearchParam });

            var searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                logger);

            var searchIndexEntries = searchIndexer.Extract(patient.ToResourceElement());

            Assert.NotEmpty(searchIndexEntries);
            var givenEntries = searchIndexEntries.Where(e => e.SearchParameter.Code == "given").ToList();
            Assert.Equal(2, givenEntries.Count);

            var givenValues = givenEntries.Select(e => e.Value as StringSearchValue).ToList();
            Assert.All(givenValues, gv => Assert.NotNull(gv));
            Assert.Contains(givenValues, gv => gv.String == "John");
            Assert.Contains(givenValues, gv => gv.String == "James");
        }
    }
}
