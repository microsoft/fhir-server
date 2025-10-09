// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
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
                try
                {
                    // Skip deleting an adit event since not allowed according to the capability statement.
                    foreach (var id in batch.Where(x => !x.StartsWith(KnownResourceTypes.AuditEvent, StringComparison.OrdinalIgnoreCase)))
                    {
                        tasks.Add(TestFhirClient.DeleteAsync(id));
                    }

                    if (tasks.Any())
                    {
                        await Task.WhenAll(tasks);
                        tasks.Clear();
                    }
                }
                catch (Exception ex)
                {
                    // NOTE: ignore any exception. Don't let a pipeline run fail.
                    Console.WriteLine($"[CreateAllFhirResourcesTestsFixture] Exception during cleanup: {ex}");
                }
            }
        }
    }
}
