// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ResourceExtensionsTests
    {
        private readonly ResourceReference _organization1 = new ResourceReference("Organization/1");
        private readonly ResourceReference _organization2 = new ResourceReference("Organization/2");
        private readonly ResourceReference _practitioner1 = new ResourceReference("Practitioner/1");
        private readonly ResourceReference _practitioner2 = new ResourceReference("Practitioner/2");
        private readonly ResourceReference _practitioner3 = new ResourceReference("Practitioner/3");
        private readonly ResourceReference _practitionerRole1 = new ResourceReference("PractitionerRole/1");
        private readonly ResourceReference _practitionerRole2 = new ResourceReference("PractitionerRole/2");

        private readonly CodeableConcept _maritalStatus = new CodeableConcept("maritalstatus", "code");
        private readonly CodeableConcept _contactRelationship1 = new CodeableConcept("relationship1", "code1");
        private readonly CodeableConcept _contactRelationship2 = new CodeableConcept("relationship2", "code2");
        private readonly CodeableConcept _contactRelationship3 = new CodeableConcept("relationship3", "code3");
        private readonly CodeableConcept _language1 = new CodeableConcept("language1", "code1");
        private readonly CodeableConcept _language2 = new CodeableConcept("language2", "code2");

        private Patient _patient;

        public ResourceExtensionsTests()
        {
            _patient = new Patient
            {
                ManagingOrganization = _organization1,
                GeneralPractitioner = new List<ResourceReference>
                {
                    _organization2,
                    _practitioner1,
                    _practitioner2,
                    _practitioner3,
                    _practitionerRole1,
                    _practitionerRole2,
                },
                MaritalStatus = _maritalStatus,
                Contact = new List<Patient.ContactComponent>
                {
                    new Patient.ContactComponent
                    {
                        Relationship = new List<CodeableConcept>
                        {
                            _contactRelationship1,
                            _contactRelationship2,
                            _contactRelationship3,
                        },
                    },
                },
                Communication = new List<Patient.CommunicationComponent>
                {
                    new Patient.CommunicationComponent
                    {
                        Language = _language1,
                    },
                    new Patient.CommunicationComponent
                    {
                        Language = _language2,
                    },
                },
            };
        }

        [Fact]
        public void GivenAResourceWithVariousReferences_WhenGettingAllChildren_CorrectChildrenAreReturned()
        {
            var resourceReferences = _patient.GetAllChildren<ResourceReference>().ToList();

            Assert.Equal(7, resourceReferences.Count);
            Assert.Contains(_organization1, resourceReferences);
            Assert.Contains(_organization2, resourceReferences);
            Assert.Contains(_practitioner1, resourceReferences);
            Assert.Contains(_practitioner2, resourceReferences);
            Assert.Contains(_practitioner3, resourceReferences);
            Assert.Contains(_practitionerRole1, resourceReferences);
            Assert.Contains(_practitionerRole2, resourceReferences);
        }

        [Fact]
        public void GivenAResourceWithVariousCodeableConcepts_WhenGettingAllChildren_CorrectChildrenAreReturned()
        {
            var codeableConcepts = _patient.GetAllChildren<CodeableConcept>().ToList();

            Assert.Equal(6, codeableConcepts.Count);

            Assert.Contains(_maritalStatus, codeableConcepts);
            Assert.Contains(_contactRelationship1, codeableConcepts);
            Assert.Contains(_contactRelationship2, codeableConcepts);
            Assert.Contains(_contactRelationship3, codeableConcepts);
            Assert.Contains(_language1, codeableConcepts);
            Assert.Contains(_language2, codeableConcepts);
        }
    }
}
