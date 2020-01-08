// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources
{
    public class ResourceExtensionsTests
    {
        private readonly CodeableConcept _codeableConcept1 = new CodeableConcept("system1", "code1");
        private readonly CodeableConcept _codeableConcept2 = new CodeableConcept("system2", "code2");
        private readonly CodeableConcept _codeableConcept3 = new CodeableConcept("system3", "code3");
        private readonly CodeableConcept _codeableConcept4 = new CodeableConcept("system4", "code4");
        private readonly CodeableConcept _codeableConcept5 = new CodeableConcept("system5", "code5");
        private readonly ResourceReference _carePlan1 = new ResourceReference("CarePlan/1");
        private readonly ResourceReference _carePlan2 = new ResourceReference("CarePlan/2");
        private readonly ResourceReference _patient1 = new ResourceReference("Patient/1");
        private readonly ResourceReference _deviceRequest1 = new ResourceReference("DeviceRequest/1");
        private readonly ResourceReference _encounter1 = new ResourceReference("Encounter/1");
        private readonly ResourceReference _encounter2 = new ResourceReference("Encounter/2");
        private readonly ResourceReference _condition1 = new ResourceReference("Condition/2");
        private readonly ResourceReference _product1 = new ResourceReference("Product/2");

        private CarePlan _carePlan;

        public ResourceExtensionsTests()
        {
            _carePlan = new CarePlan
            {
                BasedOn = new List<ResourceReference>
                {
                    _carePlan1,
                    _carePlan2,
                },
                Subject = _patient1,
                Category = new List<CodeableConcept> { _codeableConcept1, _codeableConcept2 },
                Activity = new List<CarePlan.ActivityComponent>
                {
                    new CarePlan.ActivityComponent { Reference = _deviceRequest1 },
                    new CarePlan.ActivityComponent
                    {
                        OutcomeReference = new List<ResourceReference>
                        {
                            _encounter1,
                            _encounter2,
                        },
                        Detail = new CarePlan.DetailComponent
                        {
                            ReasonReference = new List<ResourceReference>
                            {
                                _condition1,
                            },
                            Product = _product1,
                        },
                    },
                    new CarePlan.ActivityComponent
                    {
                        OutcomeCodeableConcept = new List<CodeableConcept> { _codeableConcept3, _codeableConcept4 },
                        Detail = new CarePlan.DetailComponent
                        {
                            Product = _codeableConcept5,
                        },
                    },
                },
                Intent = CarePlan.CarePlanIntent.Proposal,
                Description = "test care plan",
            };
        }

        [Fact]
        public void GivenAResourceWithVariousReferences_WhenGettingAllChildren_CorrectChildrenAreReturned()
        {
            var resourceReferences = _carePlan.GetAllChildren<ResourceReference>().ToList();

            Assert.Equal(8, resourceReferences.Count);

            Assert.Contains(_carePlan1, resourceReferences);
            Assert.Contains(_carePlan2, resourceReferences);
            Assert.Contains(_patient1, resourceReferences);
            Assert.Contains(_deviceRequest1, resourceReferences);
            Assert.Contains(_encounter1, resourceReferences);
            Assert.Contains(_encounter2, resourceReferences);
            Assert.Contains(_condition1, resourceReferences);
            Assert.Contains(_product1, resourceReferences);
        }

        [Fact]
        public void GivenAResourceWithVariousCodeableConcepts_WhenGettingAllChildren_CorrectChildrenAreReturned()
        {
            var codeableConcepts = _carePlan.GetAllChildren<CodeableConcept>().ToList();

            Assert.Equal(5, codeableConcepts.Count);

            Assert.Contains(_codeableConcept1, codeableConcepts);
            Assert.Contains(_codeableConcept2, codeableConcepts);
            Assert.Contains(_codeableConcept3, codeableConcepts);
            Assert.Contains(_codeableConcept4, codeableConcepts);
            Assert.Contains(_codeableConcept5, codeableConcepts);
        }
    }
}
