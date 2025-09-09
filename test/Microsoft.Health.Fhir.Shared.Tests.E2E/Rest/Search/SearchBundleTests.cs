// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class SearchBundleTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        private const string XForwardedHost = "X-Forwarded-Host";
        private const string XForwardedPrefix = "X-Forwarded-Prefix";
        private const string Host = "e2e.tests.fhir.microsoft.com";
        private const string Prefix = "/search/bundle/tests";

        public SearchBundleTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenSearch_WhenIncludingForwardedHeaders_ThenModifyResponseUrls()
        {
            // Create various resources.
            string tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => p.Name = new List<HumanName>
                {
                    new HumanName
                    {
                        Given = new List<string> { "Ex" },
                        Family = "Ample",
                        Suffix = new List<string> { "Jr."},
                    },
                });

            Client.HttpClient.DefaultRequestHeaders.Add(XForwardedHost, Host);
            Client.HttpClient.DefaultRequestHeaders.Add(XForwardedPrefix, Prefix);
            Bundle searchset = await Client.SearchAsync(ResourceType.Patient, count: 1);

            AssertSingletonPatientBundle(searchset);
        }

        [Fact]
        public async Task GivenSearchBundle_WhenIncludingForwardedHeaders_ThenModifyResponseUrls()
        {
            // Create various resources.
            string tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => p.Name = new List<HumanName>
                {
                    new HumanName
                    {
                        Given = new List<string> { "Ex" },
                        Family = "Ample",
                        Suffix = new List<string> { "Sr."},
                    },
                });

            Bundle batch = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>
                {
                    new Bundle.EntryComponent
                    {
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.GET,
                            Url = "Patient?_count=1",
                        },
                    },
                },
            };

            Client.HttpClient.DefaultRequestHeaders.Add(XForwardedHost, Host);
            Client.HttpClient.DefaultRequestHeaders.Add(XForwardedPrefix, Prefix);
            Bundle batchResponse = await Client.PostBundleAsync(batch);

            Bundle.EntryComponent responseEntry = Assert.Single(batchResponse.Entry);
            Bundle searchset = Assert.IsType<Bundle>(responseEntry.Response);
            AssertSingletonPatientBundle(searchset);
        }

        private static void AssertSingletonPatientBundle(Bundle searchset)
        {
            Assert.Equal(Bundle.BundleType.Searchset, searchset.Type);
            AssertBundleUri("/Patient", searchset.SelfLink);
            AssertBundleUri("/Patient", searchset.NextLink);

            Bundle.EntryComponent actual = searchset.Entry.Single();
            Patient p = Assert.IsType<Patient>(actual.Resource);
            AssertBundleUri($"/Patient/{p.Id}", new Uri(actual.FullUrl));
        }

        private static void AssertBundleUri(string expectedPath, Uri actual)
        {
            UriBuilder builder = new UriBuilder(actual);
            Assert.Equal(Host, builder.Host);
            Assert.Equal(Prefix + expectedPath, builder.Path);
        }
    }
}
