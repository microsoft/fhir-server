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

        [Fact]
        public async System.Threading.Tasks.Task GivenAnOverlongBareReferenceIdAndASingleTargetType_WhenExtract_ThenTheOverlongIdLengthIsKept()
        {
            ISearchIndexer searchIndexer = await CreateReferenceSearchIndexer("subject", "Observation.subject", "Patient");
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Subject = new ResourceReference(new string('a', 65));

            ReferenceSearchValue value = Assert.IsType<ReferenceSearchValue>(Assert.Single(searchIndexer.Extract(observation.ToResourceElement())).Value);

            Assert.Equal("Patient", value.ResourceType);
            Assert.Equal(65, value.OverlongResourceIdLength);
            Assert.Equal(64, value.MaxResourceIdLength);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async System.Threading.Tasks.Task GivenAValidAndAnOverlongReferenceWithTheSameTruncatedId_WhenExtract_ThenTheOverlongIdLengthIsKept(bool validFirst)
        {
            ISearchIndexer searchIndexer = await CreateReferenceSearchIndexer("performer", "Observation.performer", "Patient", "Practitioner");
            string validId = new string('a', 64);
            var references = new List<ResourceReference> { new ResourceReference("Patient/" + validId), new ResourceReference("Patient/" + validId + "x") };
            if (!validFirst)
            {
                references.Reverse();
            }

            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Performer = references;

            ReferenceSearchValue value = Assert.IsType<ReferenceSearchValue>(Assert.Single(searchIndexer.Extract(observation.ToResourceElement())).Value);

            Assert.Equal(validId, value.ResourceId);
            Assert.Equal(65, value.OverlongResourceIdLength);
            Assert.Equal(64, value.MaxResourceIdLength);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenReferencesThatDifferOnlyInCase_WhenExtract_ThenTheFirstIdIsIndexedWithTheOverlongLength()
        {
            // Reference values compare ids case-insensitively, but SQL Server indexes them as written, so the first one must be kept.
            ISearchIndexer searchIndexer = await CreateReferenceSearchIndexer("performer", "Observation.performer", "Patient", "Practitioner");
            string firstId = new string('A', 64);
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Performer = new List<ResourceReference> { new ResourceReference("Practitioner/" + firstId), new ResourceReference("Practitioner/" + new string('a', 65)) };

            ReferenceSearchValue value = Assert.IsType<ReferenceSearchValue>(Assert.Single(searchIndexer.Extract(observation.ToResourceElement())).Value);

            Assert.Equal(firstId, value.ResourceId);
            Assert.Equal("Practitioner", value.ResourceType);
            Assert.Equal(65, value.OverlongResourceIdLength);
            Assert.Equal(64, value.MaxResourceIdLength);
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

        private async System.Threading.Tasks.Task<ISearchIndexer> CreateReferenceSearchIndexer(string code, string expression, params string[] targetResourceTypes)
        {
            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            supportedSearchParameterDefinitionManager.GetSearchParameters(Arg.Any<string>()).Returns(new[]
            {
                new SearchParameterInfo(
                    code,
                    code,
                    ValueSets.SearchParamType.Reference,
                    new Uri("http://hl7.org/fhir/SearchParameter/Observation-" + code),
                    expression: expression,
                    targetResourceTypes: targetResourceTypes,
                    baseResourceTypes: new List<string> { "Observation" }),
            });

            return new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                await GetTypeConverterAsync(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                Substitute.For<ILogger<TypedElementSearchIndexer>>());
        }
    }
}
