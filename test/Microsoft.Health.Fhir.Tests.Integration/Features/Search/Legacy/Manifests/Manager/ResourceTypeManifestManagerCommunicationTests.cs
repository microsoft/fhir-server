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
    public class ResourceTypeManifestManagerCommunicationTests : ResourceTypeManifestManagerTests<Communication>
    {
        private readonly Communication _communication = new Communication();

        protected override Communication Resource => _communication;

        [Fact]
        public void GivenACommunicationWithBasedOn_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "based-on",
                () =>
                {
                    _communication.BasedOn = new List<ResourceReference>
                    {
                        new ResourceReference(PatientReference),
                        new ResourceReference(LocationReference),
                    };
                },
                ValidateReference,
                PatientReference,
                LocationReference);
        }

        [Fact]
        public void GivenACommunicationWithCategory_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "category",
                () =>
                {
                    _communication.Category = new List<CodeableConcept> { CodeableConcept1WithText };
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenACommunicationWithContext_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "context",
                () => { _communication.Context = new ResourceReference(EncounterReference); },
                ValidateReference,
                EncounterReference);
        }

        [Fact]
        public void GivenACommunicationWithDefinition_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "definition",
                () =>
                {
                    _communication.Definition = new List<ResourceReference>
                    {
                        new ResourceReference(PlanDefinitionReference),
                        new ResourceReference(ActivityDefinitionReference),
                    };
                },
                ValidateReference,
                PlanDefinitionReference,
                ActivityDefinitionReference);
        }

        [Fact]
        public void GivenACommunicationWithEncounterContext_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "encounter",
                () => { _communication.Context = new ResourceReference(EncounterReference); },
                ValidateReference,
                EncounterReference);
        }

        [Fact]
        public void GivenACommunicationWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(c => c.Identifier);
        }

        [Fact]
        public void GivenACommunicationWithMedium_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "medium",
                () =>
                {
                    _communication.Medium = new List<CodeableConcept> { CodeableConcept1WithText };
                },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenACommunicationWithPartOf_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "part-of",
                () =>
                {
                    _communication.PartOf = new List<ResourceReference>
                    {
                        new ResourceReference(PlanDefinitionReference),
                        new ResourceReference(ActivityDefinitionReference),
                    };
                },
                ValidateReference,
                PlanDefinitionReference,
                ActivityDefinitionReference);
        }

        [Fact]
        public void GivenACommunicationWithPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () => { _communication.Subject = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenACommunicationWithReceived_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "received",
                () =>
                {
                    _communication.ReceivedElement = new FhirDateTime(DateTime1);
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenACommunicationWithRecipient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "recipient",
                () =>
                {
                    _communication.Recipient = new List<ResourceReference>
                    {
                        new ResourceReference(PatientReference),
                        new ResourceReference(PractitionerReference),
                    };
                },
                ValidateReference,
                PatientReference,
                PractitionerReference);
        }

        [Fact]
        public void GivenACommunicationWithSender_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "sender",
                () => { _communication.Sender = new ResourceReference(PractitionerReference); },
                ValidateReference,
                PractitionerReference);
        }

        [Fact]
        public void GivenACommunicationWithSent_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "sent",
                () =>
                {
                    _communication.SentElement = new FhirDateTime(DateTime1);
                },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenACommunicationWithStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "status",
                () => { _communication.Status = EventStatus.InProgress; },
                new Coding("http://hl7.org/fhir/event-status", "in-progress"));
        }

        [Fact]
        public void GivenACommunicationWithSubject_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "subject",
                () => { _communication.Subject = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }
    }
}
