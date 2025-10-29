// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class ReferenceRemoverTests
    {
        [Fact]
        public void GivenResourceWithReference_WhenReferenceIsRemoved_ThenReferenceIsRemoved()
        {
            var reference = KnownResourceTypes.Practitioner + "/testRef";
            var patient = new Patient();
            patient.GeneralPractitioner.Add(new ResourceReference(reference));

            var id = "testPatient";
            patient.Id = id;

            var name = new HumanName() { Family = "Testson" };
            patient.Name.Add(name);

            ReferenceRemover.RemoveReference(patient, reference);

            Assert.True(patient.GeneralPractitioner.First().Reference == null);
            Assert.Equal("Referenced resource deleted", patient.GeneralPractitioner.First().Display);
            Assert.Equal(id, patient.Id);
            Assert.Equal(name, patient.Name.First());
        }

        [Fact]
        public void GivenResourceWithoutReference_WhenReferenceIsRemoved_ThenNothingIsChanged()
        {
            var referenceId = "testRef";
            var patient = new Patient();

            var id = "testPatient";
            patient.Id = id;

            var name = new HumanName() { Family = "Testson" };
            patient.Name.Add(name);

            ReferenceRemover.RemoveReference(patient, referenceId);

            Assert.Equal(id, patient.Id);
            Assert.Equal(name, patient.Name.First());
        }

        [Fact]
        public void GivenResourceWithReferenceDescription_WhenReferenceIsRemoved_ThenNothingIsChanged()
        {
            var referenceId = "testRef";
            var patient = new Patient();

            var reference = new ResourceReference();
            reference.Display = referenceId;
            patient.GeneralPractitioner.Add(reference);

            var id = "testPatient";
            patient.Id = id;

            var name = new HumanName() { Family = "Testson" };
            patient.Name.Add(name);

            ReferenceRemover.RemoveReference(patient, referenceId);

            Assert.True(patient.GeneralPractitioner.First().Reference == null);
            Assert.Equal(referenceId, patient.GeneralPractitioner.First().Display);
            Assert.Equal(id, patient.Id);
            Assert.Equal(name, patient.Name.First());
        }

        [Fact]
        public void GivenResourceWithTwoReferences_WhenReferenceIsRemoved_ThenOtherReferenceIsNotChanged()
        {
            var reference = KnownResourceTypes.Practitioner + "/testRef";
            var otherReference = KnownResourceTypes.Practitioner + "/other";
            var patient = new Patient();
            patient.GeneralPractitioner.Add(new ResourceReference(reference));
            patient.GeneralPractitioner.Add(new ResourceReference(otherReference));

            var id = "testPatient";
            patient.Id = id;

            var name = new HumanName() { Family = "Testson" };
            patient.Name.Add(name);

            ReferenceRemover.RemoveReference(patient, reference);

            Assert.True(patient.GeneralPractitioner[0].Reference == null);
            Assert.Equal("Referenced resource deleted", patient.GeneralPractitioner[0].Display);

            Assert.Equal(otherReference, patient.GeneralPractitioner[1].Reference);
            Assert.True(patient.GeneralPractitioner[1].Display == null);

            Assert.Equal(id, patient.Id);
            Assert.Equal(name, patient.Name.First());
        }
    }
}
