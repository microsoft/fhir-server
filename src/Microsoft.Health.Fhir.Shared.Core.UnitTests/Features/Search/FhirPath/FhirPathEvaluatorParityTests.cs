// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search.FhirPath
{
    /// <summary>
    /// Parity tests between the Firely and Ignixa <see cref="IFhirPathEvaluator"/> implementations.
    /// </summary>
    /// <remarks>
    /// Search indexing is the seam where a FHIRPath difference is most damaging: the two engines must agree, or
    /// the search index silently diverges depending on which provider indexed a resource. Because there is no
    /// runtime fallback from Ignixa to Firely, an expression Ignixa cannot compile would mean permanently
    /// missing index entries, so expression coverage is asserted here rather than discovered in production.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirPathEvaluatorParityTests
    {
        private const string PatientJson = """
            {
              "resourceType": "Patient",
              "id": "patient-1",
              "meta": { "versionId": "3", "lastUpdated": "1990-01-02T03:04:05.123+00:00" },
              "active": true,
              "name": [
                { "use": "official", "family": "Chalmers", "given": [ "Peter", "James" ] },
                { "use": "usual", "given": [ "Jim" ] }
              ],
              "telecom": [ { "system": "phone", "value": "555-1234", "use": "home" } ],
              "gender": "male",
              "birthDate": "1974-12-25",
              "address": [ { "city": "PleasantVille", "state": "Vic", "postalCode": "3999" } ],
              "managingOrganization": { "reference": "Organization/1" }
            }
            """;

        private const string ObservationJson = """
            {
              "resourceType": "Observation",
              "id": "obs-1",
              "status": "final",
              "code": { "coding": [ { "system": "http://loinc.org", "code": "29463-7" } ], "text": "Weight" },
              "subject": { "reference": "Patient/patient-1" },
              "effectiveDateTime": "1990-01-02",
              "valueQuantity": { "value": 72.5, "unit": "kg", "system": "http://unitsofmeasure.org", "code": "kg" }
            }
            """;

        private static readonly IFhirPathEvaluator FirelyEvaluator = new FirelyFhirPathEvaluator();
        private static readonly IFhirPathEvaluator IgnixaEvaluator = new IgnixaFhirPathEvaluator();

        static FhirPathEvaluatorParityTests()
        {
            // Matches FhirModule startup: the Firely engine resolves resolve()/ofType()/hasValue() from this table.
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        public static TheoryData<string, string> ExpressionCases()
        {
            var data = new TheoryData<string, string>();

            foreach (string expression in new[]
            {
                "Patient.name",
                "Patient.name.family",
                "Patient.name.given",
                "Patient.active",
                "Patient.birthDate",
                "Patient.gender",
                "Patient.telecom",
                "Patient.telecom.where(system='phone')",
                "Patient.address.city",
                "Patient.name.where(use='official').family",
                "Patient.managingOrganization",
                "Patient.id",
                "Patient.meta.lastUpdated",
                "Patient.name.given | Patient.name.family",
                "Patient.name.first().family",
                "Patient.name.exists()",
                "Patient.telecom.count()",
                "Patient.deceased.exists()",
                "Patient.birthDate.toString()",
            })
            {
                data.Add(expression, PatientJson);
            }

            foreach (string expression in new[]
            {
                "Observation.status",
                "Observation.code",
                "Observation.subject",
                "Observation.value.ofType(Quantity)",
                "Observation.code.coding.code",
                "Observation.code.coding.where(system='http://loinc.org')",
            })
            {
                data.Add(expression, ObservationJson);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(ExpressionCases))]
        public void GivenAnExpression_WhenEvaluatedByBothEngines_ThenTheResultsMatch(string expression, string json)
        {
            ITypedElement element = Parse(json);
            var context = new EvaluationContext { Resource = element, RootResource = element };

            IReadOnlyList<ITypedElement> firely = FirelyEvaluator.Compile(expression).Evaluate(element, context).ToList();
            IReadOnlyList<ITypedElement> ignixa = IgnixaEvaluator.Compile(expression).Evaluate(element, context).ToList();

            Assert.Equal(firely.Count, ignixa.Count);

            for (int i = 0; i < firely.Count; i++)
            {
                // The converter manager dispatches on InstanceType, so a mismatch here would silently route a
                // value to a different search value converter, or to none at all.
                Assert.Equal(firely[i].InstanceType, ignixa[i].InstanceType);
                Assert.Equal(Describe(firely[i]), Describe(ignixa[i]));
            }
        }

        [Theory]
        [InlineData("Patient.name.exists()", "System.Boolean")]
        [InlineData("Patient.telecom.count()", "System.Integer")]
        [InlineData("Patient.deceased.exists()", "System.Boolean")]
        [InlineData("Patient.name.family.first() + 'x'", "System.String")]
        [InlineData("Patient.telecom.count() + 1", "System.Integer")]
        public void GivenAComputedValue_WhenEvaluatedByIgnixa_ThenItReportsTheSameSystemTypeAsFirely(
            string expression,
            string expectedInstanceType)
        {
            // FHIRPath defines function results as system types. Firely names them System.Boolean/System.Integer;
            // Ignixa natively reports the FHIR primitive name, so SystemTypedElementAdapter restores the Firely
            // naming. Every entry in that adapter's map must be justified by a case here: mapping a primitive to
            // a system name Firely does not itself produce would create divergence rather than remove it.
            //
            // Note this does not currently change converter selection - BooleanToTokenSearchValueConverter and
            // friends register both spellings. The point is that parity is a property of this seam rather than a
            // coincidence of the converter registration table.
            ITypedElement element = Parse(PatientJson);
            var context = new EvaluationContext { Resource = element, RootResource = element };

            ITypedElement firely = Assert.Single(FirelyEvaluator.Compile(expression).Evaluate(element, context));
            ITypedElement ignixa = Assert.Single(IgnixaEvaluator.Compile(expression).Evaluate(element, context));

            Assert.Equal(expectedInstanceType, firely.InstanceType);
            Assert.Equal(expectedInstanceType, ignixa.InstanceType);
        }

        [Fact]
        public void GivenASchemaBoundPrimitive_WhenEvaluatedByIgnixa_ThenItKeepsTheFhirTypeName()
        {
            // The system-type mapping must not leak into values read straight off the resource: Patient.active
            // is a FHIR boolean element, and both engines must keep reporting it as "boolean" so the boolean
            // converter is still selected.
            ITypedElement element = Parse(PatientJson);
            var context = new EvaluationContext { Resource = element, RootResource = element };

            ITypedElement firely = Assert.Single(FirelyEvaluator.Compile("Patient.active").Evaluate(element, context));
            ITypedElement ignixa = Assert.Single(IgnixaEvaluator.Compile("Patient.active").Evaluate(element, context));

            Assert.Equal("boolean", firely.InstanceType);
            Assert.Equal("boolean", ignixa.InstanceType);
        }

        [Fact]
        public async Task GivenEverySupportedSearchParameter_WhenCompiledByIgnixa_ThenNoExpressionIsRejected()
        {
            string[] expressions = await GetAllExpressionsAsync();

            Assert.NotEmpty(expressions);

            var failures = new List<string>();

            foreach (string expression in expressions)
            {
                try
                {
                    IgnixaEvaluator.Compile(expression);
                }
                catch (Exception ex)
                {
                    failures.Add($"{expression} -> {ex.GetType().Name}: {ex.Message}");
                }
            }

            string message =
                $"Ignixa failed to compile {failures.Count} of {expressions.Length} search parameter expressions:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, failures.Take(25));

            Assert.True(failures.Count == 0, message);
        }

        private static async Task<string[]> GetAllExpressionsAsync()
        {
            SearchParameterDefinitionManager definitionManager =
                await SearchParameterFixtureData.CreateSearchParameterDefinitionManagerAsync(
                    new VersionSpecificModelInfoProvider(),
                    Substitute.For<Medino.IMediator>());

            return definitionManager.AllSearchParameters
                .Select(x => x.Expression)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static ITypedElement Parse(string json)
        {
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);

            return Deserializers.ResourceDeserializer
                .DeserializeRaw(rawResource, "1", DateTimeOffset.UtcNow)
                .Instance;
        }

        /// <summary>
        /// Renders an element as a comparable string. Primitives compare by type and value. Complex elements
        /// carry no value of their own, so they are rendered recursively: comparing only the set of child names
        /// would let two engines agree on shape while disagreeing on every value underneath.
        /// </summary>
        private static string Describe(ITypedElement element)
        {
            if (element.Value != null)
            {
                return $"{element.InstanceType}={Convert.ToString(element.Value, CultureInfo.InvariantCulture)}";
            }

            var children = element.Children()
                .Select(child => $"{child.Name}:{Describe(child)}")
                .ToList();

            return $"{element.InstanceType}{{{string.Join(",", children)}}}";
        }
    }
}
