// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestManagerAppointmentTests : ResourceTypeManifestManagerTests<Appointment>
    {
        private readonly Appointment _appointment = new Appointment();

        protected override Appointment Resource => _appointment;

        [Fact]
        public void GivenAnAppointmentWithActor_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "actor",
                () =>
                {
                    _appointment.Participant = new List<Hl7.Fhir.Model.Appointment.ParticipantComponent>
                    {
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(PatientReference),
                        },
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(LocationReference),
                        },
                    };
                },
                ValidateReference,
                PatientReference,
                LocationReference);
        }

        [Fact]
        public void GivenAnAppointmentWithAppointmentType_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "appointment-type",
                () => { _appointment.AppointmentType = CodeableConcept1WithText; },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnAppointmentWithADate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "date",
                () => { _appointment.Start = Instant1; },
                ValidateInstant,
                Instant1);
        }

        [Fact]
        public void GivenAnAppointmentWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(a => a.Identifier);
        }

        [Fact]
        public void GivenAnAppointmentWithIncomingReferral_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "incomingreferral",
                () =>
                {
                    _appointment.IncomingReferral = new List<ResourceReference>
                    {
                        new ResourceReference(ReferralRequestReference1),
                        new ResourceReference(ReferralRequestReference2),
                    };
                },
                ValidateReference,
                ReferralRequestReference1,
                ReferralRequestReference2);
        }

        [Fact]
        public void GivenAnAppointmentWithLocation_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "location",
                () =>
                {
                    _appointment.Participant = new List<Hl7.Fhir.Model.Appointment.ParticipantComponent>
                    {
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(PatientReference),
                        },
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(LocationReference),
                        },
                    };
                },
                ValidateReference,
                LocationReference);
        }

        [Fact]
        public void GivenAnAppointmentWithPartStatusType_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "part-status",
                () =>
                {
                    _appointment.Participant = new List<Hl7.Fhir.Model.Appointment.ParticipantComponent>
                    {
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(PatientReference),
                            Status = ParticipationStatus.Accepted,
                        },
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(LocationReference),
                        },
                    };
                },
                new Coding("http://hl7.org/fhir/participationstatus", "accepted"));
        }

        [Fact]
        public void GivenAnAppointmentWithPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () =>
                {
                    _appointment.Participant = new List<Hl7.Fhir.Model.Appointment.ParticipantComponent>
                    {
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(PatientReference),
                        },
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(LocationReference),
                        },
                    };
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAnAppointmentWithPractitioner_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "practitioner",
                () =>
                {
                    _appointment.Participant = new List<Hl7.Fhir.Model.Appointment.ParticipantComponent>
                    {
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(PatientReference),
                        },
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(LocationReference),
                        },
                        new Hl7.Fhir.Model.Appointment.ParticipantComponent
                        {
                            Actor = new ResourceReference(PractitionerReference),
                        },
                    };
                },
                ValidateReference,
                PractitionerReference);
        }

        [Fact]
        public void GivenAnAppointmentWithServiceType_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "service-type",
                () =>
                {
                    _appointment.ServiceType = new List<CodeableConcept> { CodeableConcept1WithText };
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAnAppointmentWithStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "status",
                () =>
                {
                    _appointment.Status = Hl7.Fhir.Model.Appointment.AppointmentStatus.Booked;
                },
                new Coding("http://hl7.org/fhir/appointmentstatus", "booked"));
        }
    }
}
