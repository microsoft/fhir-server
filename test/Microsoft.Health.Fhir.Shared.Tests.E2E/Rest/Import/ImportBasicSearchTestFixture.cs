// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportBasicSearchTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportBasicSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            PatientAddressCityAndFamily.Address = new List<Address>()
                {
                    new Address() { City = Guid.NewGuid().ToString("N") },
                };
            PatientAddressCityAndFamily.Name = new List<HumanName>()
                {
                    new HumanName() { Family = Guid.NewGuid().ToString("N") },
                };

            string cityName = Guid.NewGuid().ToString("N");
            PatientWithSameCity1.Address = new List<Address>()
                {
                    new Address() { City = cityName },
                };
            PatientWithSameCity2.Address = new List<Address>()
                {
                    new Address() { City = cityName },
                };
        }

        public Patient PatientAddressCityAndFamily { get; set; } = new Patient() { Id = Guid.NewGuid().ToString("N") };

        public Patient PatientWithSameCity1 { get; set; } = new Patient() { Id = Guid.NewGuid().ToString("N") };

        public Patient PatientWithSameCity2 { get; set; } = new Patient() { Id = Guid.NewGuid().ToString("N") };

        public Patient PatientWithGender { get; set; } = new Patient() { Id = Guid.NewGuid().ToString("N"), Gender = AdministrativeGender.Male };

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        protected override async Task OnInitializedAsync()
        {
            await ImportTestHelper.ImportToServerAsync(
                TestFhirClient,
                StorageAccount,
                PatientAddressCityAndFamily.AddTestTag(FixtureTag),
                PatientWithSameCity1.AddTestTag(FixtureTag),
                PatientWithSameCity2.AddTestTag(FixtureTag),
                PatientWithGender.AddTestTag(FixtureTag));
        }
    }
}
