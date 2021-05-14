// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportBasicSearchTestFixture : ImportTestFixture<StartupForImportBasicSearchTestProvider>
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();

        public ImportBasicSearchTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            PatientAddressCityAndFamily.Address = new List<Address>()
                {
                    new Address() { City = Guid.NewGuid().ToString("N") },
                };
            PatientAddressCityAndFamily.Name = new List<HumanName>()
                {
                    new HumanName() { Family = Guid.NewGuid().ToString("N") },
                };
        }

        public Patient PatientAddressCityAndFamily { get; set; } = new Patient() { Id = Guid.NewGuid().ToString("N") };

        public async Task InitailizeAsync()
        {
            await ImportToServerAsync(PatientAddressCityAndFamily);
        }

        public async Task ImportToServerAsync(params Resource[] resources)
        {
            Dictionary<string, StringBuilder> contentBuilders = new Dictionary<string, StringBuilder>();

            foreach (Resource resource in resources)
            {
                string resourceType = resource.ResourceType.ToString();
                if (!contentBuilders.ContainsKey(resourceType))
                {
                    contentBuilders[resourceType] = new StringBuilder();
                }

                contentBuilders[resourceType].AppendLine(_fhirJsonSerializer.SerializeToString(resource));
            }

            var inputFiles = new List<InputResource>();
            foreach ((string key, StringBuilder builder) in contentBuilders)
            {
                (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(builder.ToString(), CloudStorageAccount);
                inputFiles.Add(new InputResource()
                {
                    Etag = etag,
                    Url = location,
                    Type = key,
                });
            }

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = inputFiles,
            };

            await ImportCheckAsync(request);
        }

        private async Task ImportCheckAsync(ImportRequest request)
        {
            Uri checkLocation = await TestFhirClient.ImportAsync(request.ToParameters());

            while ((await TestFhirClient.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
