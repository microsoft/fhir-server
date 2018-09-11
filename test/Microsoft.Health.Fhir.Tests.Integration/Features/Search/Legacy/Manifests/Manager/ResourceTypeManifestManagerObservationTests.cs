// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestManagerObservationTests : ResourceTypeManifestManagerTests<Observation>
    {
        private readonly Observation _observation = new Observation();

        protected override Observation Resource => _observation;

        [Fact]
        public void GivenAnObservationWithBasedOn_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "based-on",
                () =>
                {
                    _observation.BasedOn = new List<ResourceReference>()
                    {
                        new ResourceReference(PatientReference),
                        new ResourceReference(OrganizationReference),
                    };
                },
                ValidateReference,
                PatientReference,
                OrganizationReference);
        }

        [Fact]
        public void GivenAnObservationWithCategory_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "category",
                () =>
                {
                    _observation.Category = new List<CodeableConcept>()
                    {
                        CodeableConcept1WithText,
                        CodeableConcept2,
                    };
                },
                CodingsForCodeableConcept1WithText,
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAnObservationWithCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "code",
                () =>
                {
                    _observation.Code = CodeableConcept1WithText;
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnObservationWithCodeValueConcept_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "code-value-concept",
                () =>
                {
                    _observation.Code = CodeableConcept1WithText;
                    _observation.Value = CodeableConcept2;
                },
                ValidateToken,
                new CompositeCombo<Coding>(Coding1WithText, Coding3WithText),
                new CompositeCombo<Coding>(Coding1WithText, Coding4),
                new CompositeCombo<Coding>(Coding2, Coding3WithText),
                new CompositeCombo<Coding>(Coding2, Coding4));
        }

        [Fact]
        public void GivenAnObservationWithCodeValueDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "code-value-date",
                () =>
                {
                    _observation.Code = CreateCodeableConcept(Coding1WithText);
                    _observation.Value = new FhirDateTime(DateTime1);
                },
                ValidateDateTime,
                new CompositeCombo<string>(Coding1WithText, DateTime1));
        }

        [Fact]
        public void GivenAnObservationWithCodeValueQuantity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "code-value-quantity",
                () =>
                {
                    _observation.Code = CreateCodeableConcept(Coding1WithText);
                    _observation.Value = Quantity1;
                },
                ValidateQuantity,
                new CompositeCombo<Quantity>(Coding1WithText, Quantity1));
        }

        [Fact]
        public void GivenAnObservationWithCodeValueString_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "code-value-string",
                () =>
                {
                    _observation.Code = CreateCodeableConcept(Coding1WithText);
                    _observation.Value = new FhirString(String1);
                },
                ValidateString,
                new CompositeCombo<string>(Coding1WithText, String1));
        }

        [Fact]
        public void GivenAnObservationWithComboCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "combo-code",
                () =>
                {
                    _observation.Code = CodeableConcept1WithText;

                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent { Code = CodeableConcept2 },
                        new Observation.ComponentComponent { Code = CreateCodeableConcept(Coding3WithText) },
                    };
                },
                CodingsForCodeableConcept1WithText,
                CodingsForCodeableConcept2,
                new[] { Coding3WithText });
        }

        [Fact]
        public void GivenAnObservationWithComboCodeValueConcept_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "combo-code-value-concept",
                () =>
                {
                    _observation.Code = CreateCodeableConcept(Coding1WithText);
                    _observation.Value = CreateCodeableConcept(Coding2);

                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding3WithText),
                            Value = CreateCodeableConcept(Coding4),
                        },
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding5),
                            Value = CreateCodeableConcept(Coding6WithText),
                        },
                    };
                },
                ValidateToken,
                new CompositeCombo<Coding>(Coding1WithText, Coding2),
                new CompositeCombo<Coding>(Coding3WithText, Coding4),
                new CompositeCombo<Coding>(Coding5, Coding6WithText));
        }

        [Fact]
        public void GivenAnObservationWithComboCodeValueQuantity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "combo-code-value-quantity",
                () =>
                {
                    _observation.Code = CreateCodeableConcept(Coding1WithText);
                    _observation.Value = Quantity1;

                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding2),
                            Value = Quantity2,
                        },
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding3WithText),
                            Value = Quantity3,
                        },
                    };
                },
                ValidateQuantity,
                new CompositeCombo<Quantity>(Coding1WithText, Quantity1),
                new CompositeCombo<Quantity>(Coding2, Quantity2),
                new CompositeCombo<Quantity>(Coding3WithText, Quantity3));
        }

        [Fact]
        public void GivenAnObservationWithComboDataAbsentReason_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "combo-data-absent-reason",
                () =>
                {
                    _observation.DataAbsentReason = CreateCodeableConcept(Coding3WithText);

                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            DataAbsentReason = CodeableConcept1WithText,
                        },
                        new Observation.ComponentComponent
                        {
                            DataAbsentReason = CodeableConcept2,
                        },
                    };
                },
                new[] { Coding3WithText },
                CodingsForCodeableConcept1WithText,
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAnObservationWithComboValueConcept_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "combo-value-concept",
                () =>
                {
                    _observation.Value = CodeableConcept1WithText;
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnObservationWithComboValueQuantity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "combo-value-quantity",
                () =>
                {
                    _observation.Value = Quantity1;
                },
                ValidateQuantity,
                Quantity1);
        }

        [Fact]
        public void GivenAnObservationWithComponentCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "component-code",
                () =>
                {
                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            Code = CodeableConcept2,
                        },
                        new Observation.ComponentComponent
                        {
                            Code = CodeableConcept1WithText,
                        },
                    };
                },
                CodingsForCodeableConcept2,
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnObservationWithComponentCodeValueConcept_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "component-code-value-concept",
                () =>
                {
                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding1WithText),
                            Value = CreateCodeableConcept(Coding2),
                        },
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding3WithText),
                            Value = CreateCodeableConcept(Coding4),
                        },
                    };
                },
                ValidateToken,
                new CompositeCombo<Coding>(Coding1WithText, Coding2),
                new CompositeCombo<Coding>(Coding3WithText, Coding4));
        }

        [Fact]
        public void GivenAnObservationWithComponentCodeValueQuantity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "component-code-value-quantity",
                () =>
                {
                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding1WithText),
                            Value = Quantity1,
                        },
                        new Observation.ComponentComponent
                        {
                            Code = CreateCodeableConcept(Coding3WithText),
                            Value = Quantity2,
                        },
                    };
                },
                ValidateQuantity,
                new CompositeCombo<Quantity>(Coding1WithText, Quantity1),
                new CompositeCombo<Quantity>(Coding3WithText, Quantity2));
        }

        [Fact]
        public void GivenAnObservationWithComponentDataAbsentReason_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "component-data-absent-reason",
                () =>
                {
                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            DataAbsentReason = CodeableConcept1WithText,
                        },
                        new Observation.ComponentComponent
                        {
                            DataAbsentReason = CodeableConcept2,
                        },
                    };
                },
                CodingsForCodeableConcept1WithText,
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAnObservationWithComponentValueConcept_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "component-value-concept",
                () =>
                {
                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            Value = CodeableConcept2,
                        },
                        new Observation.ComponentComponent
                        {
                            Value = CodeableConcept1WithText,
                        },
                    };
                },
                CodingsForCodeableConcept2,
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnObservationWithComponentValueQuantity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "component-value-quantity",
                () =>
                {
                    _observation.Component = new List<Observation.ComponentComponent>
                    {
                        new Observation.ComponentComponent
                        {
                            Value = Quantity1,
                        },
                        new Observation.ComponentComponent
                        {
                            Value = Quantity2,
                        },
                    };
                },
                ValidateQuantity,
                Quantity1,
                Quantity2);
        }

        [Fact]
        public void GivenAnObservationWithContext_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "context",
                () =>
                {
                    _observation.Context = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnObservationWithDataAbsentReason_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "data-absent-reason",
                () =>
                {
                    _observation.DataAbsentReason = CodeableConcept1WithText;
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnObservationWithEffectiveAsDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "date",
                () =>
                {
                    _observation.Effective = new FhirDateTime(DateTime1);
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAnObservationWithEffectiveAsPeriod_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "date",
                () =>
                {
                    _observation.Effective = Period1;
                },
                ValidateDateTime,
                "2018");
        }

        [Fact]
        public void GivenAnObservationWithDevice_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "device",
                () =>
                {
                    _observation.Device = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnObservationWithEncounter_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            string reference = "Encounter/123";

            TestSearchParam(
                "encounter",
                () =>
                {
                    _observation.Context = new ResourceReference(reference);
                },
                ValidateReference,
                reference);

            // Reference that's not an encounter
            TestSearchParam(
                "encounter",
                () =>
                {
                    _observation.Context = new ResourceReference("Patient/123");
                },
                ValidateReference,
                new string[0]);
        }

        [Fact]
        public void GivenAnObservationWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(o => o.Identifier);
        }

        [Fact]
        public void GivenAnObservationWithMethod_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "method",
                () =>
                {
                    _observation.Method = CodeableConcept2;
                },
                CodingsForCodeableConcept2);
        }

        [Fact]
        public void GivenAnObservationWithSubjectThatIsPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () =>
                {
                    _observation.Subject = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnObservationWithSubjectThatIsNotPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () =>
                {
                    _observation.Subject = new ResourceReference(OrganizationReference);
                },
                ValidateReference,
                new string[0]);
        }

        [Fact]
        public void GivenAnObservationWithPerformer_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "performer",
                () =>
                {
                    _observation.Performer = new List<ResourceReference>
                    {
                        new ResourceReference(PatientReference),
                        new ResourceReference(OrganizationReference),
                    };
                },
                ValidateReference,
                PatientReference,
                OrganizationReference);
        }

        [Fact]
        public void GivenAnObservationWithRelated_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestCompositeSearchParam(
                "related",
                () =>
                {
                    _observation.Related = new List<Observation.RelatedComponent>
                    {
                        new Observation.RelatedComponent()
                        {
                            Type = Observation.ObservationRelationshipType.DerivedFrom,
                            Target = new ResourceReference(PatientReference),
                        },
                        new Observation.RelatedComponent()
                        {
                            Type = Observation.ObservationRelationshipType.HasMember,
                            Target = new ResourceReference(OrganizationReference),
                        },
                    };
                },
                ValidateReference,
                new CompositeCombo<string>(new Coding("http://hl7.org/fhir/observation-relationshiptypes", "derived-from"), PatientReference),
                new CompositeCombo<string>(new Coding("http://hl7.org/fhir/observation-relationshiptypes", "has-member"), OrganizationReference));
        }

        [Fact]
        public void GivenAnObservationWithRelatedTarget_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "related-target",
                () =>
                {
                    _observation.Related = new List<Observation.RelatedComponent>
                    {
                        new Observation.RelatedComponent()
                        {
                            Type = Observation.ObservationRelationshipType.DerivedFrom,
                            Target = new ResourceReference(PatientReference),
                        },
                        new Observation.RelatedComponent()
                        {
                            Type = Observation.ObservationRelationshipType.HasMember,
                            Target = new ResourceReference(OrganizationReference),
                        },
                    };
                },
                ValidateReference,
                PatientReference,
                OrganizationReference);
        }

        [Fact]
        public void GivenAnObservationWithRelatedType_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "related-type",
                () =>
                {
                    _observation.Related = new List<Observation.RelatedComponent>
                    {
                        new Observation.RelatedComponent()
                        {
                            Type = Observation.ObservationRelationshipType.DerivedFrom,
                            Target = new ResourceReference(PatientReference),
                        },
                        new Observation.RelatedComponent()
                        {
                            Type = Observation.ObservationRelationshipType.HasMember,
                            Target = new ResourceReference(OrganizationReference),
                        },
                    };
                },
                new Coding("http://hl7.org/fhir/observation-relationshiptypes", "derived-from"),
                new Coding("http://hl7.org/fhir/observation-relationshiptypes", "has-member"));
        }

        [Fact]
        public void GivenAnObservationWithSpecimen_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "specimen",
                () =>
                {
                    _observation.Specimen = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnObservationWithStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "status",
                () =>
                {
                    _observation.Status = ObservationStatus.Final;
                },
                new Coding("http://hl7.org/fhir/observation-status", "final"));
        }

        [Fact]
        public void GivenAnObservationWithSubject_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "subject",
                () =>
                {
                    _observation.Subject = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnObservationWithValueConcept_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "value-concept",
                () =>
                {
                    _observation.Value = CodeableConcept1WithText;
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnObservationWithValueDateFromDateTime_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "value-date",
                () =>
                {
                    _observation.Value = new FhirDateTime(DateTime1);
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAnObservationWithValueDateFromPeriod_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "value-date",
                () =>
                {
                    _observation.Value = Period1;
                },
                ValidateDateTime,
                "2018");
        }

        [Fact]
        public void GivenAnObservationWithValueQuantity_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "value-quantity",
                () =>
                {
                    _observation.Value = Quantity1;
                },
                ValidateQuantity,
                Quantity1);
        }

        [Fact]
        public void GivenAnObservationWithValueString_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "value-string",
                () =>
                {
                    _observation.Value = new FhirString(String1);
                },
                ValidateString,
                String1);
        }

        private void TestCompositeSearchParam<TValue>(string paramName, Action setup, Action<TValue, ISearchValue> validator, params CompositeCombo<TValue>[] expected)
        {
            setup();

            IEnumerable<ISearchValue> values = Manifest.GetSearchParam(paramName).ExtractValues(_observation);

            Assert.NotNull(values);

            var validators = new List<Action<ISearchValue>>();

            foreach (var combo in expected)
            {
                validators.Add(new Action<ISearchValue>(
                    sv =>
                    {
                        ValidateComposite(combo.Coding, combo.Value, validator, sv);
                    }));
            }

            Assert.Collection(
                values,
                validators.ToArray());
        }

        private class CompositeCombo<TValue>
        {
            public CompositeCombo(Coding coding, TValue value)
            {
                Coding = coding;
                Value = value;
            }

            public Coding Coding { get; }

            public TValue Value { get; }
        }
    }
}
