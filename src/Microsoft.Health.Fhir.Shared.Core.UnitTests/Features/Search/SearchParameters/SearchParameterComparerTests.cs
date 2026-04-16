// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search.SearchParameters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterComparerTests
    {
        private readonly ISearchParameterComparer<SearchParameterInfo> _comparer;
        private readonly ITestOutputHelper _output;

        public SearchParameterComparerTests(ITestOutputHelper output)
        {
            _comparer = new SearchParameterComparer(Substitute.For<ILogger<ISearchParameterComparer<SearchParameterInfo>>>());
            _output = output;
        }

        [Theory]
        [MemberData(nameof(GetCompareBaseData))]
        public void GivenBases_WhenComparing_ThenCorrectResultShouldBeReturn(
            string[] base1,
            string[] base2,
            int result)
        {
            Assert.Equal(result, _comparer.CompareBase(base1, base2));
        }

        [Theory]
        [MemberData(nameof(GetCompareComponentData))]
        public void GivenComponents_WhenComparing_ThenCorrectResultShouldBeReturn(
            IDictionary<string, string> component1,
            IDictionary<string, string> component2,
            int result)
        {
            Assert.Equal(
                result,
                _comparer.CompareComponent(
                    component1.Select<KeyValuePair<string, string>, (string, string)>(x => new(x.Key, x.Value)),
                    component2.Select<KeyValuePair<string, string>, (string, string)>(x => new(x.Key, x.Value))));
        }

        [Fact]
        public void GivenExpressions_WhenComparing_ThenComparerShouldBeAbleToParseAndCompare()
        {
            // Note: this test ensures that the compare can parse and compare all expressiosn in search-parameters.json.
            // The comparison logic is tested by other tests in this class.
            var fhirCoreAssembly = Assembly.Load("Microsoft.Health.Fhir.Core");

#if R4B
            var specification = FhirSpecification.R4B;
#elif R4
            var specification = FhirSpecification.R4;
#elif Stu3
            var specification = FhirSpecification.Stu3;
#else
            var specification = FhirSpecification.R5;
#endif

            var name = $"Microsoft.Health.Fhir.Core.Data.{specification}.search-parameters.json";
            using var stream = fhirCoreAssembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);

            using (var reader = new StreamReader(stream))
            {
                var content = reader.ReadToEnd();
                Assert.NotNull(content);

                var rawResource = new RawResource(content, FhirResourceFormat.Json, true);
                var bundle = rawResource.ToITypedElement(ModelInfoProvider.Instance).ToPoco() as Bundle;
                Assert.NotNull(bundle);

                foreach (var entry in bundle.Entry)
                {
                    var searchParameter = entry.Resource as SearchParameter;
                    Assert.NotNull(searchParameter);

                    if (!string.IsNullOrEmpty(searchParameter.Expression))
                    {
                        try
                        {
                            DebugOutput($"Comparing an expression '{searchParameter.Expression}'...");
                            Assert.Equal(0, _comparer.CompareExpression(searchParameter.Expression, searchParameter.Expression));
                            DebugOutput($"Comparing an expression succeeded.");
                        }
                        catch (Exception ex)
                        {
                            DebugOutput($"Failed to compare an expression '{searchParameter.Expression}':{Environment.NewLine}{ex}");
                        }
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(GetCompareExpressionData))]
        public void GivenExpressions_WhenComparing_ThenCorrectResultShouldBeReturn(
            string expression1,
            string expression2,
            bool baseTypeExpression,
            int result)
        {
            Assert.Equal(result, _comparer.CompareExpression(expression1, expression2, baseTypeExpression));
        }

        [Theory]
        [MemberData(nameof(GetCompareSearchParameterData))]
        public void GivenSearchParameters_WhenComparing_ThenCorrectResultShouldBeReturn(
            SearchParameterInfo searchParameter1,
            SearchParameterInfo searchParameter2,
            int result)
        {
            Assert.Equal(result, _comparer.Compare(searchParameter1, searchParameter2));
        }

        public static IEnumerable<object[]> GetCompareBaseData()
        {
            var data = new[]
            {
                new object[]
                {
                    new string[] { "Resource" },
                    new string[] { "Resource" },
                    0,
                },
                new object[]
                {
                    new string[] { "Resource" },
                    new string[] { "DomainResource" },
                    int.MinValue,
                },
                new object[]
                {
                    new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "Citation" },
                    new string[] { "CapabilityStatement", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                    0,
                },
                new object[]
                {
                    new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "Citation" },
                    new string[] { "CapabilityStatement", "ChargeItemDefinition", "ActivityDefinition" },
                    1,
                },
                new object[]
                {
                    new string[] { "ActivityDefinition", "Citation" },
                    new string[] { "CapabilityStatement", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                    -1,
                },
                new object[]
                {
                    new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "TerminologyCapabilities", "Citation" },
                    new string[] { "CapabilityStatement", "ImplementationGuide", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                    int.MinValue,
                },
                new object[]
                {
                    new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "Citation" },
                    new string[] { },
                    1,
                },
                new object[]
                {
                    new string[] { },
                    new string[] { "CapabilityStatement", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                    -1,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetCompareComponentData()
        {
            var data = new[]
            {
                new object[]
                {
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    0,
                },
                new object[]
                {
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                    },
                    0,
                },
                new object[]
                {
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    int.MinValue,
                },
                new object[]
                {
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity)"
                        },
                    },
                    int.MinValue,
                },
                new object[]
                {
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/conformance-context-type",
                            "code"
                        },
                    },
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    1,
                },
                new object[]
                {
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/conformance-context-type",
                            "code"
                        },
                    },
                    -1,
                },
                new object[]
                {
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                    },
                    new Dictionary<string, string>
                    {
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-quantity",
                            "value.as(Quantity) | value.as(Range)"
                        },
                        {
                            "http://hl7.org/fhir/SearchParameter/ActivityDefinition-context-type",
                            "code"
                        },
                    },
                    0,
                },
                new object[]
                {
                    new Dictionary<string, string>
                    {
                    },
                    new Dictionary<string, string>
                    {
                    },
                    0,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetCompareExpressionData()
        {
            var data = new[]
            {
                new object[]
                {
                    "AllergyIntolerance.code",
                    "AllergyIntolerance.code",
                    false,
                    0,
                },
                new object[]
                {
                    "AdverseEvent.event",
                    "AdverseEvent.location",
                    false,
                    int.MinValue,
                },
                new object[]
                {
                    "ActivityDefinition.relatedArtifact.where(type='composed-of').resource",
                    "ActivityDefinition.relatedArtifact.where(type='composed-of').resource",
                    false,
                    0,
                },
                new object[]
                {
                    "ActivityDefinition.relatedArtifact.where(type='composed-of').resource",
                    "ActivityDefinition.relatedArtifact.where(type='depends-on').resource",
                    false,
                    int.MinValue,
                },
                new object[]
                {
                    "AllergyIntolerance.code | (MedicationStatement.medication as CodeableConcept)",
                    "(MedicationStatement.medication as CodeableConcept) | AllergyIntolerance.code",
                    false,
                    0,
                },
                new object[]
                {
                    "(ActivityDefinition.useContext.value as Quantity) | (ActivityDefinition.useContext.value as Range)",
                    "ActivityDefinition.useContext.value as Range",
                    false,
                    1,
                },
                new object[]
                {
                    "PlanDefinition.library | EvidenceVariable.relatedArtifact.where(type='depends-on').resource | ActivityDefinition.library | Library.relatedArtifact.where(type='depends-on').resource | Measure.relatedArtifact.where(type='depends-on').resource | Measure.library",
                    "ActivityDefinition.relatedArtifact.where(type='depends-on').resource | ActivityDefinition.library | EventDefinition.relatedArtifact.where(type='depends-on').resource | EvidenceVariable.relatedArtifact.where(type='depends-on').resource | Library.relatedArtifact.where(type='depends-on').resource | Measure.relatedArtifact.where(type='depends-on').resource | Measure.library | PlanDefinition.relatedArtifact.where(type='depends-on').resource | PlanDefinition.library",
                    false,
                    -1,
                },
                new object[]
                {
                    "CareTeam.name | CareTeam.participant.member | CareTeam.extension('http://hl7.org/fhir/StructureDefinition/careteam-alias').value",
                    "CareTeam.name | CareTeam.participant.category | CareTeam.extension('http://hl7.org/fhir/StructureDefinition/careteam-alias').value",
                    false,
                    int.MinValue,
                },
                new object[]
                {
                    "Practitioner.id",
                    "Resource.id",
                    true,
                    1,
                },
                new object[]
                {
                    "Resource.meta.tag",
                    "Patient.meta.tag",
                    true,
                    -1,
                },
                new object[]
                {
                    "DomainResource.text",
                    "Observation.text",
                    true,
                    -1,
                },
                new object[]
                {
                    "Practitioner.ids",
                    "Resource.id",
                    true,
                    int.MinValue,
                },
                new object[]
                {
                    "Resource.meta.tag",
                    "DomainResource.meta.tag",
                    true,
                    -1,
                },
                new object[]
                {
                    "Resource.type",
                    "Resource.type",
                    true,
                    0,
                },
                new object[]
                {
                    "Patient.id",
                    "Practitioner.id",
                    true,
                    int.MinValue,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetCompareSearchParameterData()
        {
            var data = new[]
            {
                new object[]
                {
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    0,
                },
                new object[]
                {
                    // Url mismatch
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author2"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    int.MinValue,
                },
                new object[]
                {
                    // Code mismatch
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "authority",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    int.MinValue,
                },
                new object[]
                {
                    // Type mismatch
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Token,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    int.MinValue,
                },
                new object[]
                {
                    // Base mismatch
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "Patient" }),
                    int.MinValue,
                },
                new object[]
                {
                    // 2nd SP with superset base
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    -1,
                },
                new object[]
                {
                    // 1st SP with superset base
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    1,
                },
                new object[]
                {
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    0,
                },
                new object[]
                {
                    // Component mismatch
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    int.MinValue,
                },
                new object[]
                {
                    // 2nd SP with superset component
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    -1,
                },
                new object[]
                {
                    // 1st SP with superset component
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    1,
                },
                new object[]
                {
                    // Expression mismatch
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author2"),
                        expression: "DocumentReference.authority",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    int.MinValue,
                },
                new object[]
                {
                    // 2nd SP with superset expression
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.authority | (DocumentReference.author)",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    -1,
                },
                new object[]
                {
                    // 1st SP with superset expression
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.authority | (DocumentReference.author)",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    1,
                },
                new object[]
                {
                    // 2nd SP with superset base and component
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    -1,
                },
                new object[]
                {
                    // 1st SP with superset base and component
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    1,
                },
                new object[]
                {
                    // 1st SP with superset component and 2nd SP with superset base
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    int.MinValue,
                },
                new object[]
                {
                    // 2nd SP with superset base and expression
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.authority | (DocumentReference.author)",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    -1,
                },
                new object[]
                {
                    // 1st SP with superset base and expression
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.authority | (DocumentReference.author)",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    1,
                },
                new object[]
                {
                    // 1st SP with superset base and 2nd SP with superset expression
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author"),
                        expression: "DocumentReference.author",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    new SearchParameterInfo(
                        name: "author",
                        code: "author",
                        searchParamType: ValueSets.SearchParamType.Reference,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-author2"),
                        expression: "DocumentReference.authority | (DocumentReference.author)",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    int.MinValue,
                },
                new object[]
                {
                    // 2nd SP with superset base, component, and expression
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo | DocumentReference.id",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    -1,
                },
                new object[]
                {
                    // 1st SP with superset base, component, and expression
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo | DocumentReference.id",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    1,
                },
                new object[]
                {
                    // 2nd SP with superset component and expression, and 1st SP with superset base
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo | DocumentReference.id",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    int.MinValue,
                },
                new object[]
                {
                    // 1st SP with superset base and component, and 2nd SP with superset expression
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-event"), "event"),
                        },
                        expression: "DocumentReference.relatesTo",
                        baseResourceTypes: new[] { "DocumentReference", "Patient" }),
                    new SearchParameterInfo(
                        name: "relationship",
                        code: "relationship",
                        searchParamType: ValueSets.SearchParamType.Composite,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relationship"),
                        components: new List<SearchParameterComponentInfo>
                        {
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relatesto"), "target"),
                            new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/DocumentReference-relation"), "code"),
                        },
                        expression: "DocumentReference.relatesTo | DocumentReference.id",
                        baseResourceTypes: new[] { "DocumentReference" }),
                    int.MinValue,
                },
                new object[]
                {
                    // 1st base-type SP, and 2nd custom SP
                    new SearchParameterInfo(
                        name: "_id",
                        code: "_id",
                        searchParamType: ValueSets.SearchParamType.Token,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"),
                        expression: "Resource.id",
                        baseResourceTypes: new[] { "Resource" }),
                    new SearchParameterInfo(
                        name: "USCorePractitionerId",
                        code: "_id",
                        searchParamType: ValueSets.SearchParamType.Token,
                        url: new Uri("http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id"),
                        expression: "Practitioner.id",
                        baseResourceTypes: new[] { "Practitioner" }),
                    -1,
                },
                new object[]
                {
                    // 1st base-type SP, and 2nd custom SP
                    new SearchParameterInfo(
                        name: "_id",
                        code: "_id",
                        searchParamType: ValueSets.SearchParamType.Token,
                        url: new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"),
                        expression: "Resource.id",
                        baseResourceTypes: new[] { "Resource" }),
                    new SearchParameterInfo(
                        name: "USCorePractitionerId",
                        code: "_id",
                        searchParamType: ValueSets.SearchParamType.Token,
                        url: new Uri("http://hl7.org/fhir/us/core/SearchParameter/us-core-practitioner-id"),
                        expression: "Practitioner.ids",
                        baseResourceTypes: new[] { "Practitioner" }),
                    int.MinValue,
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        private void DebugOutput(string message)
        {
#if true
            _output.WriteLine(message);
#endif
        }
    }
}
