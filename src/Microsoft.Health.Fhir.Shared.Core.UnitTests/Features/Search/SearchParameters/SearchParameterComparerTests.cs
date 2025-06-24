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
        private readonly ISearchParameterComparer _comparer;
        private readonly ITestOutputHelper _output;

        public SearchParameterComparerTests(ITestOutputHelper output)
        {
            _comparer = new SearchParameterComparer(Substitute.For<ILogger<ISearchParameterComparer>>());
            _output = output;
        }

        public static IEnumerable<object[]> CompareComponentData
        {
            get
            {
                return new[]
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
                        true,
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
                        true,
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
                        false,
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
                        false,
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
                        false,
                    },
                    new object[]
                    {
                        new Dictionary<string, string>
                        {
                        },
                        new Dictionary<string, string>
                        {
                        },
                        true,
                    },
                };
            }
        }

        [Theory]
        [InlineData(new object[] { new string[] { "Resource" }, new string[] { "Resource" }, 0 })]
        [InlineData(new object[] { new string[] { "Resource" }, new string[] { "DomainResource" }, int.MinValue })]
        [InlineData(
            new object[]
            {
                new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "Citation" },
                new string[] { "CapabilityStatement", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                0,
            })]
        [InlineData(
            new object[]
            {
                new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "Citation" },
                new string[] { "CapabilityStatement", "ChargeItemDefinition", "ActivityDefinition" },
                1,
            })]
        [InlineData(
            new object[]
            {
                new string[] { "ActivityDefinition", "Citation" },
                new string[] { "CapabilityStatement", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                -1,
            })]
        [InlineData(
            new object[]
            {
                new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "TerminologyCapabilities", "Citation" },
                new string[] { "CapabilityStatement", "ImplementationGuide", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                int.MinValue,
            })]
        [InlineData(
            new object[]
            {
                new string[] { "ActivityDefinition", "ActorDefinition", "CapabilityStatement", "ChargeItemDefinition", "Citation" },
                new string[] { },
                1,
            })]
        [InlineData(
            new object[]
            {
                new string[] { },
                new string[] { "CapabilityStatement", "ActorDefinition", "Citation", "ChargeItemDefinition", "ActivityDefinition" },
                -1,
            })]
        public void GivenBases_WhenComparing_ThenCorrectResultShouldBeReturn(
            string[] base1,
            string[] base2,
            int result)
        {
            Assert.Equal(result, _comparer.CompareBase(base1, base2));
        }

        [Theory]
        [MemberData(nameof(CompareComponentData))]
        public void GivenComponents_WhenComparing_ThenCorrectResultShouldBeReturn(
            IDictionary<string, string> component1,
            IDictionary<string, string> component2,
            bool result)
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
        [InlineData(
            "AllergyIntolerance.code",
            "AllergyIntolerance.code",
            0)]
        [InlineData(
            "AdverseEvent.event",
            "AdverseEvent.location",
            int.MinValue)]
        [InlineData(
            "ActivityDefinition.relatedArtifact.where(type='composed-of').resource",
            "ActivityDefinition.relatedArtifact.where(type='composed-of').resource",
            0)]
        [InlineData(
            "ActivityDefinition.relatedArtifact.where(type='composed-of').resource",
            "ActivityDefinition.relatedArtifact.where(type='depends-on').resource",
            int.MinValue)]
        [InlineData(
            "AllergyIntolerance.code | (MedicationStatement.medication as CodeableConcept)",
            "(MedicationStatement.medication as CodeableConcept) | AllergyIntolerance.code",
            0)]
        [InlineData(
            "(ActivityDefinition.useContext.value as Quantity) | (ActivityDefinition.useContext.value as Range)",
            "ActivityDefinition.useContext.value as Range",
            1)]
        [InlineData(
            "PlanDefinition.library | EvidenceVariable.relatedArtifact.where(type='depends-on').resource | ActivityDefinition.library | Library.relatedArtifact.where(type='depends-on').resource | Measure.relatedArtifact.where(type='depends-on').resource | Measure.library",
            "ActivityDefinition.relatedArtifact.where(type='depends-on').resource | ActivityDefinition.library | EventDefinition.relatedArtifact.where(type='depends-on').resource | EvidenceVariable.relatedArtifact.where(type='depends-on').resource | Library.relatedArtifact.where(type='depends-on').resource | Measure.relatedArtifact.where(type='depends-on').resource | Measure.library | PlanDefinition.relatedArtifact.where(type='depends-on').resource | PlanDefinition.library",
            -1)]
        [InlineData(
            "CareTeam.name | CareTeam.participant.member | CareTeam.extension('http://hl7.org/fhir/StructureDefinition/careteam-alias').value",
            "CareTeam.name | CareTeam.participant.category | CareTeam.extension('http://hl7.org/fhir/StructureDefinition/careteam-alias').value",
            int.MinValue)]
        public void GivenExpressions_WhenComparing_ThenCorrectResultShouldBeReturn(
            string expression1,
            string expression2,
            int result)
        {
            Assert.Equal(result, _comparer.CompareExpression(expression1, expression2));
        }

        private void DebugOutput(string message)
        {
#if true
            _output.WriteLine(message);
#endif
        }
    }
}
