// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides R4 resource creation tests.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public partial class CreateAllFhirResourcesTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public CreateAllFhirResourcesTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Theory]
        [MemberData(nameof(GetFullResourceFileNames))]
        public async Task GivenValidResources_WhenCreateIsCalled_ShouldBeCreatedSuccessfully(object fileName)
        {
           var resourceJson = GetJsonSample((string)fileName).ToPoco();
           using var resource = await _client.CreateAsync(resourceJson);
           Assert.Equal(System.Net.HttpStatusCode.Created, resource.StatusCode);
        }

        public static IEnumerable<object[]> GetFullResourceFileNames()
        {
            // yield return new object[] { "account-example" };
            // yield return new object[] { "appointment-example" };

            // Iterate through the folder and get all the embedded resource file names
            foreach (var fullFileName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                var parts = fullFileName.Split('.');
                var extension = parts[^1];            // "json"
                var fileName = parts[^2];
                yield return new object[] { fileName };
            }
        }

        private static ResourceElement GetJsonSample(string fileName)
        {
            var fhirSource = GetDataFromFile(fileName);
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            return parser.Parse<Hl7.Fhir.Model.Resource>(fhirSource).ToTypedElement().ToResourceElement();
        }

        private static string GetDataFromFile(string fileName)
        {
            string resourceName = $"Microsoft.Health.Fhir.Tests.E2E.TestFiles.{fileName}.json";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
