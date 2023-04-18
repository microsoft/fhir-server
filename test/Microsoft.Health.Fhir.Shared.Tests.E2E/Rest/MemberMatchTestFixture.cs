// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public sealed class MemberMatchTestFixture : HttpIntegrationTestFixture<Startup>
    {
        private readonly Coding _tag;

        public MemberMatchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            _tag = new Coding(string.Empty, Guid.NewGuid().ToString());
        }

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            // Create various resources.
            Patient[] patients = await TestFhirClient.CreateResourcesAsync<Patient>(
                p => SetPatient(p, "Seattle", "Robinson", "01", "1970-01-01"),
                p => SetPatient(p, "Portland", "Williamas", "02", "1970-01-02"),
                p => SetPatient(p, "Seattle", "Jones", "03", "1970-02-01"));

            Coverage[] coverage = await TestFhirClient.CreateResourcesAsync<Coverage>(
                c => SetCoverage(c, patients[0], "EHCPOL", "P0"),
                c => SetCoverage(c, patients[1], "DENTPRG", "P8"),
                c => SetCoverage(c, patients[2], "EHCPOL", "P9"),
                c => SetCoverage(c, patients[0], "EHCPOL", "P8"),
                c => SetCoverage(c, patients[2], "EHCPOL", "P1"),
                c => SetCoverage(c, patients[1], "EHCPOL", "P2"));
        }

        public void SetPatient(Patient patient, string city = null, string family = null, string identifier = null, string birthDate = null)
        {
            patient.Meta = new Meta()
            {
                Tag = new List<Coding>() { _tag },
            };
            if (!string.IsNullOrEmpty(city))
            {
                patient.Address = new List<Address>()
                    {
                        new Address() { City = city },
                    };
            }

            if (!string.IsNullOrEmpty(family))
            {
                patient.Name = new List<HumanName>()
                    {
                        new HumanName() { Family = family },
                    };
            }

            if (!string.IsNullOrEmpty(identifier))
            {
                patient.Identifier = new List<Identifier>()
                    {
                        new Identifier("someHospital", identifier) { Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v2-0203", "MB", "Medical  number") },
                        new Identifier("someHospital", "record") { Type = new CodeableConcept("http://hl7.org/fhir/v2/0203", "MR", "Medical  number") },
                    };
            }

            patient.BirthDate = birthDate;
        }

        public void SetCoverage(Coverage coverage, Patient patient, string type, string subPlan = null)
        {
            coverage.Meta = new Meta()
            {
                Tag = new List<Coding>() { _tag },
            };
            coverage.Beneficiary = new ResourceReference($"Patient/{patient.Id}");
#if Stu3 || R4 || R4B
            coverage.Payor = new List<ResourceReference> { new ResourceReference($"Patient/{patient.Id}") };
#endif
#if R5
            coverage.PaymentBy = new List<Coverage.PaymentByComponent>() { new Coverage.PaymentByComponent() { Party = new ResourceReference($"Patient/{patient.Id}") } };
#endif
            coverage.Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v3-ActCode", type);
            coverage.Status = FinancialResourceStatusCodes.Active;
            if (!string.IsNullOrEmpty(subPlan))
            {
#if !Stu3
                coverage.Class = new List<Coverage.ClassComponent>()
                {
                    new Coverage.ClassComponent()
                    {
                        Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/coverage-class", "subplan"),
#if R4 || R4B
                        Value = subPlan,
#else
                        Value = new Identifier() { Value = subPlan },
#endif
                    },
                };
#else
                coverage.Grouping = new Coverage.GroupComponent()
                {
                    SubPlan = subPlan,
                };
#endif
            }
        }
    }
}
