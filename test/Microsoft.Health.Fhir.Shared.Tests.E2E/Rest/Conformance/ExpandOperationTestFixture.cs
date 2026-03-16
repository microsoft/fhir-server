// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Conformance
{
    public class ExpandOperationTestFixture : HttpIntegrationTestFixture
    {
        // Note: this file contains only one of these valuesets. Add more to it as needed.
        // https://hl7.org/fhir/R4/terminologies-valuesets.html
        private const string ExpandTestFileName = "Expand-ValueSets";

        private readonly List<ValueSet> _valueSets;
        private readonly string _tag;

        public ExpandOperationTestFixture(
            DataStore dataStore,
            Format format,
            TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            _valueSets = new List<ValueSet>();
            _tag = $"expandtest-{DateTime.UtcNow.Ticks}";
        }

        public IReadOnlyList<ValueSet> ValueSets => _valueSets;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            var bundle = TagResources(Samples.GetJsonSample<Bundle>(ExpandTestFileName));
            var response = await TestFhirClient.PostBundleAsync(bundle);
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response?.Resource?.Entry);
            Assert.NotEmpty(response.Resource.Entry);

            var resourceCreated = response.Resource.Entry.Select(x => (ValueSet)x.Resource);
            Assert.All(resourceCreated, x => Assert.Null(x.Expansion));
            _valueSets.AddRange(resourceCreated);
        }

        private Resource TagResources(Bundle bundle)
        {
            foreach (var entry in bundle.Entry)
            {
                entry.Resource.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", _tag),
                    },
                };
            }

            return bundle;
        }
    }
}
