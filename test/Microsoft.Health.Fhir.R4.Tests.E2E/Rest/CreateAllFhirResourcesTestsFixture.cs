// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class CreateAllFhirResourcesTestsFixture : HttpIntegrationTestFixture
    {
        public CreateAllFhirResourcesTestsFixture(
            DataStore dataStore,
            Format format,
            TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public List<string> ResourcesToCleanup { get; } = new List<string>();

        protected override async Task OnDisposedAsync()
        {
            await base.OnDisposedAsync();

            var batches = ResourcesToCleanup
                .Select((r, i) => new { r, i })
                .GroupBy(x => x.i / 10)
                .Select(x => x.Select(y => y.r))
                .ToList();
            var tasks = new List<Task>();
            foreach (var batch in batches)
            {
                foreach (var id in batch)
                {
                    tasks.Add(TestFhirClient.DeleteAsync(id));
                }

                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }
    }
}
