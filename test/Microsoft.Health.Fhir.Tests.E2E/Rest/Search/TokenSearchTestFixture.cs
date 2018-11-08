// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class TokenSearchTestFixture : SearchTestFixture
    {
        public IReadOnlyList<Observation> Observations { get; private set; }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            Observations = await CreateResourcesAsync<Observation>(
                o => o.Identifier,
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
                o => SetObservation(o, cc => cc.Coding.Add(new Coding(null, "code3"))));

            void SetObservation(Observation observation, Action<CodeableConcept> codeableConceptCustomizer)
            {
                observation.Code = new CodeableConcept("system", "code");
                observation.Status = ObservationStatus.Registered;

                var codeableConcept = new CodeableConcept();

                codeableConceptCustomizer(codeableConcept);

                observation.Value = codeableConcept;
            }
        }
    }
}
