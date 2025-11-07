// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Health.Fhir.ValueSets;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{

    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class CapabilityStatementExtractorTests
    {
        private readonly CapabilityStatementExtractor _extractor;

        public CapabilityStatementExtractorTests()
        {
            _extractor = new CapabilityStatementExtractor();
        }

        [Fact]
        public void GivenNullCapabilityStatement_WhenGettingResourceTypes_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _extractor.GetResourceTypes(null));
        }

        [Fact]
        public void GivenEmptyCapabilityStatement_WhenGettingResourceTypes_ThenReturnsEmptyCollection()
        {
            var capabilityStatement = new ListedCapabilityStatement();

            var result = _extractor.GetResourceTypes(capabilityStatement);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenCapabilityStatementWithResources_WhenGettingResourceTypes_ThenReturnsResourceTypes()
        {
            var capabilityStatement = CreateCapabilityStatementWithResources("Patient", "Observation", "Condition");

            var result = _extractor.GetResourceTypes(capabilityStatement).ToList();

            Assert.Equal(3, result.Count);
            Assert.Contains("Patient", result);
            Assert.Contains("Observation", result);
            Assert.Contains("Condition", result);
        }

        [Fact]
        public void GivenCapabilityStatementWithDuplicateResources_WhenGettingResourceTypes_ThenReturnsDistinctResourceTypes()
        {
            var capabilityStatement = new ListedCapabilityStatement();
            var rest1 = new ListedRestComponent { Mode = "server" };
            var rest2 = new ListedRestComponent { Mode = "client" };
            
            rest1.Resource.Add(new ListedResourceComponent { Type = "Patient" });
            rest2.Resource.Add(new ListedResourceComponent { Type = "Patient" });
            
            capabilityStatement.Rest.Add(rest1);
            capabilityStatement.Rest.Add(rest2);

            var result = _extractor.GetResourceTypes(capabilityStatement).ToList();

            Assert.Single(result);
            Assert.Equal("Patient", result[0]);
        }

        [Fact]
        public void GivenNullCapabilityStatement_WhenGettingSearchParametersForResource_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => 
                _extractor.GetSearchParametersForResource(null, "Patient"));
        }

        [Fact]
        public void GivenNullResourceType_WhenGettingSearchParametersForResource_ThenThrowsArgumentException()
        {
            var capabilityStatement = new ListedCapabilityStatement();

            Assert.Throws<ArgumentException>(() => 
                _extractor.GetSearchParametersForResource(capabilityStatement, null));
        }

        [Fact]
        public void GivenEmptyResourceType_WhenGettingSearchParametersForResource_ThenThrowsArgumentException()
        {
            var capabilityStatement = new ListedCapabilityStatement();

            Assert.Throws<ArgumentException>(() => 
                _extractor.GetSearchParametersForResource(capabilityStatement, string.Empty));
        }

        [Fact]
        public void GivenResourceWithSearchParams_WhenGettingSearchParametersForResource_ThenReturnsSearchParameters()
        {
            var capabilityStatement = new ListedCapabilityStatement();
            var rest = new ListedRestComponent { Mode = "server" };
            var resource = new ListedResourceComponent { Type = "Patient" };
            
            resource.SearchParam.Add(new SearchParamComponent
            {
                Name = "name",
                Type = SearchParamType.String,
                Definition = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Documentation = "A portion of either family or given name of the patient"
            });
            
            resource.SearchParam.Add(new SearchParamComponent
            {
                Name = "birthdate",
                Type = SearchParamType.Date,
                Definition = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                Documentation = "The patient's date of birth"
            });
            
            rest.Resource.Add(resource);
            capabilityStatement.Rest.Add(rest);

            var result = _extractor.GetSearchParametersForResource(capabilityStatement, "Patient").ToList();

            Assert.Equal(2, result.Count);
            
            var nameParam = result.FirstOrDefault(p => p.Name == "name");
            Assert.NotNull(nameParam);
            Assert.Equal(SearchParamType.String, nameParam.Type);
            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-name", nameParam.Definition);
            Assert.Equal("A portion of either family or given name of the patient", nameParam.Documentation);
            
            var birthdateParam = result.FirstOrDefault(p => p.Name == "birthdate");
            Assert.NotNull(birthdateParam);
            Assert.Equal(SearchParamType.Date, birthdateParam.Type);
        }

        [Fact]
        public void GivenResourceType_WhenGettingSearchParametersForNonExistentResource_ThenReturnsEmptyCollection()
        {
            var capabilityStatement = CreateCapabilityStatementWithResources("Patient");

            var result = _extractor.GetSearchParametersForResource(capabilityStatement, "Observation");

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenResourceTypeCaseInsensitive_WhenGettingSearchParametersForResource_ThenReturnsSearchParameters()
        {
            var capabilityStatement = new ListedCapabilityStatement();
            var rest = new ListedRestComponent { Mode = "server" };
            var resource = new ListedResourceComponent { Type = "Patient" };
            
            resource.SearchParam.Add(new SearchParamComponent
            {
                Name = "name",
                Type = SearchParamType.String
            });
            
            rest.Resource.Add(resource);
            capabilityStatement.Rest.Add(rest);

            var result = _extractor.GetSearchParametersForResource(capabilityStatement, "patient").ToList();

            Assert.Single(result);
            Assert.Equal("name", result[0].Name);
        }

        [Fact]
        public void GivenNullCapabilityStatement_WhenGettingAllSearchParameters_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _extractor.GetAllSearchParameters(null));
        }

        [Fact]
        public void GivenEmptyCapabilityStatement_WhenGettingAllSearchParameters_ThenReturnsEmptyDictionary()
        {
            var capabilityStatement = new ListedCapabilityStatement();

            var result = _extractor.GetAllSearchParameters(capabilityStatement);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenCapabilityStatementWithMultipleResources_WhenGettingAllSearchParameters_ThenReturnsDictionaryWithAllResources()
        {
            var capabilityStatement = new ListedCapabilityStatement();
            var rest = new ListedRestComponent { Mode = "server" };
            
            var patientResource = new ListedResourceComponent { Type = "Patient" };
            patientResource.SearchParam.Add(new SearchParamComponent
            {
                Name = "name",
                Type = SearchParamType.String
            });
            patientResource.SearchParam.Add(new SearchParamComponent
            {
                Name = "birthdate",
                Type = SearchParamType.Date
            });
            
            var observationResource = new ListedResourceComponent { Type = "Observation" };
            observationResource.SearchParam.Add(new SearchParamComponent
            {
                Name = "code",
                Type = SearchParamType.Token
            });
            
            rest.Resource.Add(patientResource);
            rest.Resource.Add(observationResource);
            capabilityStatement.Rest.Add(rest);

            var result = _extractor.GetAllSearchParameters(capabilityStatement);

            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("Patient"));
            Assert.True(result.ContainsKey("Observation"));
            
            Assert.Equal(2, result["Patient"].Count());
            Assert.Equal(1, result["Observation"].Count());
        }

        [Fact]
        public void GivenSearchParameterInfo_WhenCreated_ThenPropertiesAreSetCorrectly()
        {
            var name = "test-param";
            var type = SearchParamType.String;
            var definition = "http://example.com/SearchParameter/test";
            var documentation = "Test documentation";

            var searchParamInfo = new CapabilityStatementExtractor.SearchParameterInfo(
                name,
                type,
                definition,
                documentation);

            Assert.Equal(name, searchParamInfo.Name);
            Assert.Equal(type, searchParamInfo.Type);
            Assert.Equal(definition, searchParamInfo.Definition);
            Assert.Equal(documentation, searchParamInfo.Documentation);
        }

        [Fact]
        public void GivenSearchParamsWithDifferentTypes_WhenGettingSearchParameters_ThenAllTypesArePreserved()
        {
            var capabilityStatement = new ListedCapabilityStatement();
            var rest = new ListedRestComponent { Mode = "server" };
            var resource = new ListedResourceComponent { Type = "Patient" };
            
            var searchParamTypes = new[]
            {
                SearchParamType.String,
                SearchParamType.Number,
                SearchParamType.Date,
                SearchParamType.Token,
                SearchParamType.Reference,
                SearchParamType.Quantity
            };

            for (int i = 0; i < searchParamTypes.Length; i++)
            {
                resource.SearchParam.Add(new SearchParamComponent
                {
                    Name = $"param{i}",
                    Type = searchParamTypes[i]
                });
            }
            
            rest.Resource.Add(resource);
            capabilityStatement.Rest.Add(rest);

            var result = _extractor.GetSearchParametersForResource(capabilityStatement, "Patient").ToList();

            Assert.Equal(searchParamTypes.Length, result.Count);
            
            for (int i = 0; i < searchParamTypes.Length; i++)
            {
                var param = result.FirstOrDefault(p => p.Name == $"param{i}");
                Assert.NotNull(param);
                Assert.Equal(searchParamTypes[i], param.Type);
            }
        }

        private ListedCapabilityStatement CreateCapabilityStatementWithResources(params string[] resourceTypes)
        {
            var capabilityStatement = new ListedCapabilityStatement();
            var rest = new ListedRestComponent { Mode = "server" };

            foreach (var resourceType in resourceTypes)
            {
                rest.Resource.Add(new ListedResourceComponent { Type = resourceType });
            }

            capabilityStatement.Rest.Add(rest);
            return capabilityStatement;
        }
    }
}
