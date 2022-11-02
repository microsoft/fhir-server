// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public sealed class ValidateTestFixture : HttpIntegrationTestFixture<Startup>
    {
        public ValidateTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
         : base(dataStore, format, testFhirServerFactory)
        {
        }

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                },
            };

            // Delete all profile related resources before starting the test suite.
            var sd = new List<string>()
            {
                "StructureDefinition-us-core-birthsex", "StructureDefinition-us-core-ethnicity", "StructureDefinition-us-core-patient",
                "StructureDefinition-us-core-race", "StructureDefinition-us-core-organization", "StructureDefinition-us-core-careplan",
            };
            foreach (var name in sd)
            {
                bundle.Entry.Add(new EntryComponent
                {
                    Request = new RequestComponent
                    {
                        Method = HTTPVerb.POST,
                        Url = "/StructureDefinition",
                    },
                    Resource = Samples.GetJsonSample<StructureDefinition>(name),
                });
                await TestFhirClient.CreateAsync(Samples.GetJsonSample<StructureDefinition>(name), $"name={name}");
            }

            var valueSets = new List<string>()
            {
                "ValueSet-detailed-ethnicity", "ValueSet-detailed-race", "ValueSet-omb-ethnicity-category",
                "ValueSet-omb-race-category", "ValueSet-us-core-birthsex", "ValueSet-us-core-narrative-status",
            };
            foreach (var name in valueSets)
            {
                bundle.Entry.Add(new EntryComponent
                {
                    Request = new RequestComponent
                    {
                        Method = HTTPVerb.POST,
                        Url = "/ValueSet",
                    },
                    Resource = Samples.GetJsonSample<ValueSet>(name),
                });
                await TestFhirClient.CreateAsync(Samples.GetJsonSample<ValueSet>(name), $"name={name}");
            }

            var codeSystem = new List<string>() { "CodeSystem-careplan-category" };
            foreach (var name in codeSystem)
            {
                bundle.Entry.Add(new EntryComponent
                {
                    Request = new RequestComponent
                    {
                        Method = HTTPVerb.POST,
                        Url = "/CodeSystem",
                    },
                    Resource = Samples.GetJsonSample<CodeSystem>(name),
                });
                await TestFhirClient.CreateAsync(Samples.GetJsonSample<CodeSystem>(name), $"name={name}");
            }

            var bundleRequest = new BundleRequest(bundle.ToResourceElement());
            var bundleResource = bundleRequest.Bundle.ToPoco<Hl7.Fhir.Model.Bundle>();
            await TestFhirClient.CreateAsync(bundleResource);
        }
    }
}
