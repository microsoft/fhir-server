// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Search;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.GraphQl
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class GraphQlTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public GraphQlTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async void GivenQueryPatients_ThenAllPatientsShouldBeReturned()
        {
            var tag = Guid.NewGuid().ToString();

            // Create various patients.
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williamas", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            var content = "query{ patients { id } }";
            var requestContent = new StringContent(content, Encoding.UTF8, "application/x-www-form-urlencoded");
            var request = await Client.HttpClient.PostAsync("/graphql", requestContent);
            var response = await request.Content.ReadAsStringAsync();

            var resValues = JsonConvert.DeserializeObject<Dictionary<string, string>>(response);

            // Assert.Equal("name", b["data"]["b"]);
        }

        private void SetPatientInfo(Patient patient, string city, string family, string tag)
        {
            if (tag != null)
            {
                patient.Meta = new Meta();
                patient.Meta.Tag.Add(new Coding(null, tag));
            }

            patient.Address = new List<Address>()
                {
                    new Address() { City = city },
                };

            patient.Name = new List<HumanName>()
                {
                    new HumanName() { Family = family },
                };
        }
    }
}
