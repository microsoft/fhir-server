// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportTokenSearchTestFixture : ImportTestFixture<StartupForImportTestProvider>
    {
        public ImportTokenSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public string FixtureTag { get; } = Guid.NewGuid().ToString();

        public IReadOnlyList<Observation> Observations { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            await TestFhirClient.CreateResourcesAsync<Patient>(p =>
            {
                p.AddTestTag(FixtureTag);
            });

            Observations = await ImportTestHelper.ImportToServerAsync<Observation>(
                TestFhirClient,
                StorageAccount,
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system1", "code1"))),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system2", "code2"))),
                o => SetObservation(o, cc => cc.Text = "text"),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system1", "code2", "text2"))),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding("system3", "code3", "text"))),
                o => SetObservation(o, cc =>
                {
                    cc.Text = "text";
                    cc.Coding.Add(new Coding("system1", "code1"));
                    cc.Coding.Add(new Coding("system3", "code2"));
                }),
                o => SetObservation(o, cc =>
                {
                    cc.Coding.Add(new Coding("system2", "code1"));
                    cc.Coding.Add(new Coding("system3", "code3", "text2"));
                }),
                o => SetObservation(o, cc => cc.Coding.Add(new Coding(null, "code3"))),
                o =>
                {
                    SetObservation(o, cc => { });
                    o.Category = new List<CodeableConcept>
                    {
                        new CodeableConcept("system", "test"),
                    };
                });

            void SetObservation(Observation observation, Action<CodeableConcept> codeableConceptCustomizer)
            {
                observation.AddTestTag(FixtureTag);
                observation.Code = new CodeableConcept("system", "code");
                observation.Status = ObservationStatus.Registered;

                var codeableConcept = new CodeableConcept();

                codeableConceptCustomizer(codeableConcept);

                observation.Value = codeableConcept;
            }
        }
    }
}
