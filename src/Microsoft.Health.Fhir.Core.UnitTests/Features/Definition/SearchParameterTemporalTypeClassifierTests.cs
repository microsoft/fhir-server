// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterTemporalTypeClassifierTests
    {
        private readonly IModelInfoProvider _model = Substitute.For<IModelInfoProvider>();

        public SearchParameterTemporalTypeClassifierTests()
        {
            _model.IsKnownResource("Slot", false).Returns(true);
            var summary = Summary(Element("start", Reference("instant")), Element("end", Reference("instant")));
            _model.StructureDefinitionSummaryProvider.Provide("Slot").Returns(summary);
        }

        [Theory]
        [InlineData("Patient", "birthDate", "date", TemporalType.Date)]
        [InlineData("Composition", "date", "dateTime", TemporalType.DateTime)]
        [InlineData("Slot", "start", "instant", TemporalType.Instant)]
        [InlineData("Schedule", "planningHorizon", "Period", TemporalType.Period)]
        public void GivenSingleTemporalLeaf_WhenClassified_ThenActiveModelTypeIsUsed(
            string resourceType,
            string elementName,
            string type,
            TemporalType expected)
        {
            _model.IsKnownResource(resourceType, false).Returns(true);
            var summary = Summary(Element(elementName, Reference(type)));
            _model.StructureDefinitionSummaryProvider.Provide(resourceType).Returns(summary);

            TemporalType result = Classify($"{resourceType}.{elementName}");

            Assert.Equal(expected, result);
        }

        [Fact]
        public void GivenMedicationRequestDosageTiming_WhenClassified_ThenTypeIsTiming()
        {
            _model.IsKnownResource("MedicationRequest", false).Returns(true);
            var medicationRequest = Summary(Element("dosageInstruction", Reference("Dosage")));
            var dosage = Summary(Element("timing", Reference("Timing")));
            _model.StructureDefinitionSummaryProvider.Provide("MedicationRequest").Returns(medicationRequest);
            _model.StructureDefinitionSummaryProvider.Provide("Dosage").Returns(dosage);

            TemporalType result = Classify("MedicationRequest.dosageInstruction.timing");

            Assert.Equal(TemporalType.Timing, result);
        }

        [Theory]
        [InlineData("string")]
        [InlineData("Instant")]
        public void GivenInvalidSlotLeafTypeMetadata_WhenClassified_ThenTypeIsUnknown(string type)
        {
            // Deliberately malformed summaries must not classify Slot.start as a supported temporal type.
            var summary = Summary(Element("start", Reference(type)));
            _model.StructureDefinitionSummaryProvider.Provide("Slot").Returns(summary);

            Assert.Equal(TemporalType.Unknown, Classify("Slot.start"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Slot")]
        [InlineData("slot.start")]
        [InlineData("SLOT.start")]
        [InlineData("Slot.Start")]
        [InlineData("Slot.unknown")]
        [InlineData("Unknown.start")]
        [InlineData("Slot.start.value")]
        [InlineData(" Slot.start")]
        [InlineData("Slot.start ")]
        [InlineData("Slot..start")]
        [InlineData("(Slot.start)")]
        [InlineData("Slot.start.first()")]
        [InlineData("Slot.start as instant")]
        [InlineData("Slot.start.ofType(instant)")]
        [InlineData("Slot.start.where($this.exists())")]
        [InlineData("Slot.start[0]")]
        public void GivenUnsupportedExpression_WhenClassified_ThenTypeIsUnknown(string expression)
        {
            TemporalType result = Classify(expression);

            Assert.Equal(TemporalType.Unknown, result);
        }

        [Theory]
        [InlineData("Slot.start | Slot.end", TemporalType.Instant)]
        [InlineData("Slot.start|Slot.end", TemporalType.Instant)]
        [InlineData("Slot.start | Slot.unknown", TemporalType.Unknown)]
        [InlineData("Slot.unknown | Slot.start", TemporalType.Unknown)]
        [InlineData("Slot.start | Slot.end.first()", TemporalType.Unknown)]
        [InlineData("Slot.start |", TemporalType.Unknown)]
        [InlineData("| Slot.start", TemporalType.Unknown)]
        [InlineData("Slot.start || Slot.end", TemporalType.Unknown)]
        public void GivenUnion_WhenClassified_ThenEveryBranchMustBeUnderstood(string expression, TemporalType expected)
        {
            TemporalType result = Classify(expression);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Slot.start | Composition.date", TemporalType.Mixed)]
        [InlineData("Slot.start | Composition.date | Slot.start", TemporalType.Mixed)]
        [InlineData("Slot.start | Composition.date | Slot.unknown", TemporalType.Unknown)]
        public void GivenDifferentKnownTemporalBranches_WhenClassified_ThenMixedRequiresCompleteProof(string expression, TemporalType expected)
        {
            _model.IsKnownResource("Composition", false).Returns(true);
            var summary = Summary(Element("date", Reference("dateTime")));
            _model.StructureDefinitionSummaryProvider.Provide("Composition").Returns(summary);

            TemporalType result = Classify(expression);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("choice")]
        [InlineData("mixed")]
        [InlineData("unresolved")]
        [InlineData("unknown-type")]
        [InlineData("empty-type")]
        [InlineData("null-type")]
        [InlineData("ambiguous")]
        public void GivenInvalidSlotStructureSummary_WhenClassified_ThenTypeIsUnknown(string scenario)
        {
            var element = Element("start", Reference("instant"));
            if (scenario == "choice")
            {
                element.IsChoiceElement.Returns(true);
            }
            else if (scenario == "mixed")
            {
                var types = new ITypeSerializationInfo[] { Reference("instant"), Reference("dateTime") };
                element.Type.Returns(types);
            }
            else if (scenario == "unknown-type")
            {
                var types = new[] { Substitute.For<ITypeSerializationInfo>() };
                element.Type.Returns(types);
            }
            else if (scenario == "empty-type")
            {
                element.Type.Returns(Array.Empty<ITypeSerializationInfo>());
            }
            else if (scenario == "null-type")
            {
                ITypeSerializationInfo[] types = null;
                element.Type.Returns(types);
            }

            IStructureDefinitionSummary summary = scenario switch
            {
                "unresolved" => null,
                "ambiguous" => Summary(element, element),
                _ => Summary(element),
            };
            _model.StructureDefinitionSummaryProvider.Provide("Slot").Returns(summary);

            TemporalType result = Classify("Slot.start");

            Assert.Equal(TemporalType.Unknown, result);
        }

        [Fact]
        public void GivenHandConstructedParameter_WhenNotLoaded_ThenClassificationIsUnknown()
        {
            var parameter = new SearchParameterInfo("start", "start", SearchParamType.Date, expression: "Slot.start");

            Assert.Equal(TemporalType.Unknown, parameter.TemporalType);
        }

        [Fact]
        public void GivenNonDateOrCompositeParameter_WhenClassified_ThenTypeIsUnknown()
        {
            var parameters = new[]
            {
                new SearchParameterInfo("start", "start", SearchParamType.String, expression: "Slot.start"),
                new SearchParameterInfo("start", "start", SearchParamType.Composite, expression: "Slot.start"),
                new SearchParameterInfo(
                    "start",
                    "start",
                    SearchParamType.Date,
                    components: new[] { new SearchParameterComponentInfo(new Uri("http://example.org/component"), "start") },
                    expression: "Slot.start"),
            };

            foreach (SearchParameterInfo parameter in parameters)
            {
                TemporalType result = SearchParameterTemporalTypeClassifier.Classify(parameter, _model);

                Assert.Equal(TemporalType.Unknown, result);
            }
        }

        private TemporalType Classify(string expression) =>
            SearchParameterTemporalTypeClassifier.Classify(
                new SearchParameterInfo("test", "test", SearchParamType.Date, expression: expression), _model);

        private static IStructureDefinitionReference Reference(string type)
        {
            var reference = Substitute.For<IStructureDefinitionReference>();
            reference.ReferredType.Returns(type);
            return reference;
        }

        private static IElementDefinitionSummary Element(string name, params ITypeSerializationInfo[] types)
        {
            var element = Substitute.For<IElementDefinitionSummary>();
            element.ElementName.Returns(name);
            element.Type.Returns(types);
            return element;
        }

        private static IStructureDefinitionSummary Summary(params IElementDefinitionSummary[] elements)
        {
            var summary = Substitute.For<IStructureDefinitionSummary>();
            summary.GetElements().Returns(elements);
            return summary;
        }
    }
}
