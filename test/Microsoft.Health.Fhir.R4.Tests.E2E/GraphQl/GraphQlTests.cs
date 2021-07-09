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
using Hl7.Fhir.Serialization;
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
        public async void GivenQueryByGet_AskingForPatientSchema_ThenPatientSchemaShouldBeReturned()
        {
            var baseUrl = "graphql/graphql?sdl";
            using var message = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            message.Headers.Host = "patient.graphql";

            using HttpResponseMessage response = await Client.HttpClient.SendAsync(message);

            response.EnsureSuccessStatusCode();

            string result = response.Content.ReadAsStringAsync().Result;
            var aux = JsonConvert.DeserializeObject(result);
            Console.WriteLine("Result: " + result);
        }

        [Fact]
        public async void GivenQueryByGet_AskingForTypesSchema_ThenTypesSchemaShouldBeReturned()
        {
            var baseUrl = "graphql/graphql?sdl";
            using var message = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            message.Headers.Host = "types.graphql";

            using HttpResponseMessage response = await Client.HttpClient.SendAsync(message);

            response.EnsureSuccessStatusCode();
        }

        // Check test with team to verify correct way to do it.
        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async void GivenQueryGetAllIdOfPatients_ThenAllIdInTheServerShouldBeReturned()
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
            StringContent httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await Client.HttpClient.PostAsync("graphql", httpContent);
            response.EnsureSuccessStatusCode();

            // Ensure 3 patients are returned.
            var responseString = await response.Content.ReadAsStringAsync();
            dynamic responseObj = JsonConvert.DeserializeObject<dynamic>(responseString);

            List<Patient> patientsResponse = ((JArray)responseObj.data.patients).Select(x => new Patient
            {
                Id = (string)x["id"],
                Name = x["name"].ToObject<List<HumanName>>(),
            }).ToList();

            bool allPatientsExist = true;
            for (int i = 0; i < patients.Length; i++)
            {
                string originalId = patients[i].Id;
                if (!patientsResponse.Exists(x => x.Id == originalId))
                {
                    allPatientsExist = false;
                    break;
                }
            }

            Assert.True(allPatientsExist);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async void GivenQueryGetPatientById_ThenPatientWithIdShouldBeReturned()
        {
            // Create one Patient resource.
            var tag = Guid.NewGuid().ToString();
            var resource = new Patient();

            SetPatientInfo(resource, "Seattle", "Robinson", tag);
            Patient patient = await Client.CreateAsync(resource);

            // Define query.
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
            StringContent httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await Client.HttpClient.PostAsync("graphql", httpContent);
            response.EnsureSuccessStatusCode();

            // Ensure Patient in response is the original.
            var responseString = await response.Content.ReadAsStringAsync();
            dynamic responseObj = JsonConvert.DeserializeObject<dynamic>(responseString).data.patientById;

            var patientResponse = new Patient
            {
                Id = responseObj.id,
            };

            Assert.Equal(patient.Id, patientResponse.Id);
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
