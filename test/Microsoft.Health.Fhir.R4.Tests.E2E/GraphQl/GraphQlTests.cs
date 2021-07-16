// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Search;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.R4.Tests.E2E.GraphQl
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class GraphQlTests : SearchTestsBase<HttpIntegrationTestFixture>
    {
        public GraphQlTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async void GivenQueryByGet_AskingForGraphQlSchema_ThenGraphQlSchemaShouldBeReturned()
        {
            using HttpResponseMessage response = await Client.HttpClient.GetAsync("graphql/graphql?sdl");
            response.EnsureSuccessStatusCode();

            string schema = response.Content.ReadAsStringAsync().Result;
            Assert.Contains("Patient", schema);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async void GivenPatients_WhenFetchingByGraphQl_ThenAllPatientsInTheServerShouldBeReturned()
        {
            // Create various resources.
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williamas", tag),
                p => SetPatientInfo(p, "Seattle", "Jones", tag));

            // Define query.
            var queryObject = new
            {
                query = @"query { 
                            patients { 
                                id
                                name {
                                    family
                                }
                            }
                        }",
                variables = new { },
            };
            string jsonString = JsonConvert.SerializeObject(queryObject);
            var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await Client.HttpClient.PostAsync("graphql", httpContent);
            response.EnsureSuccessStatusCode();

            // Ensure 3 patients are returned.
            var responseString = await response.Content.ReadAsStringAsync();
            dynamic responseObj = JsonConvert.DeserializeObject<dynamic>(responseString);

            var patientsResponse = ((JArray)responseObj.data.patients).Select(x => new Patient
            {
                Id = (string)x["id"],
                Name = x["name"].ToObject<List<HumanName>>(),
            }).ToList();

            Assert.NotNull(patientsResponse);
        }

        [Fact]
        public async void GivenAPatient_WhenFetchingById_ThenPatientWithSpecifiedIdShouldBeReturned()
        {
            // Create one Patient resource.
            var tag = Guid.NewGuid().ToString();
            var resource = new Patient();

            SetPatientInfo(resource, "Seattle", "Robinson", tag);
            Patient patient = await Client.CreateAsync(resource);

            // Define query
            var queryObject = new
            {
                query = @"query($id: String) { 
                            patientById(id: $id) { 
                                id
                            }
                        }",
                variables = new
                {
                    id = patient.Id,
                },
            };
            string jsonString = JsonConvert.SerializeObject(queryObject);
            var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await Client.HttpClient.PostAsync("graphql", httpContent);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            dynamic responseObj = JsonConvert.DeserializeObject<dynamic>(responseString).data.patientById;

            // Ensure Patient in response has same id than original
            Assert.Equal(patient.Id, (string)responseObj.id);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async void GivenTwoPatients_WhenFetchingByIdAtSameTime_ThenPatientsWithSpecifiedIdShouldBeReturned()
        {
            // Create one Patient resource
            var tag = Guid.NewGuid().ToString();
            Patient[] patients = await Client.CreateResourcesAsync<Patient>(
                p => SetPatientInfo(p, "Seattle", "Robinson", tag),
                p => SetPatientInfo(p, "Portland", "Williamas", tag));

            // Define query
            var queryObject = new
            {
                query = @"query($idA: String, $idB: String) { 
                            a: patientById(id: $idA) { 
                                id
                            }
                            b: patientById(id: $idB) {
                                id
                                name {
                                    family
                                }
                            }
                        }",
                variables = new
                {
                    idA = patients[0].Id,
                    idB = patients[1].Id,
                },
            };
            string jsonString = JsonConvert.SerializeObject(queryObject);
            var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await Client.HttpClient.PostAsync("graphql", httpContent);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            dynamic responseObj = JsonConvert.DeserializeObject<dynamic>(responseString).data;

            // Ensure Patient in response has same id than original
            Assert.Equal(patients[0].Id, (string)responseObj.a.id);
            Assert.Equal(patients[1].Id, (string)responseObj.b.id);
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
