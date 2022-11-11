// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class ChainingSortAndSearchValidationTestFixture : SearchTestsBase<HttpIntegrationTestFixture>
    {
        private const string BundleFileName = "Bundle-ChainingSortAndSearchValidation";

        public ChainingSortAndSearchValidationTestFixture(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        protected async Task<IReadOnlyList<Bundle.EntryComponent>> GetHealthEntryComponentsAsync(CancellationToken cancellationToken)
        {
            string requestBundleAsString = Samples.GetJson(BundleFileName);
            var parser = new FhirJsonParser();
            var requestBundle = parser.Parse<Bundle>(requestBundleAsString);

            using FhirResponse<Bundle> fhirResponse = await Client.PostBundleAsync(requestBundle, cancellationToken);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            var recordIdentifiers = new List<HealthRecordIdentifier>();

            // Ensure all records were ingested.
            Assert.Equal(requestBundle.Entry.Count, fhirResponse.Resource.Entry.Count);

            return fhirResponse.Resource.Entry;
        }

        protected async Task<IReadOnlyList<HealthRecordIdentifier>> GetHealthRecordIdentifiersAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<Bundle.EntryComponent> entries = await GetHealthEntryComponentsAsync(cancellationToken);

            List<HealthRecordIdentifier> recordIdentifiers = new List<HealthRecordIdentifier>();
            foreach (Bundle.EntryComponent component in entries)
            {
                Assert.NotNull(component.Response.Status);
                HttpStatusCode httpStatusCode = (HttpStatusCode)Convert.ToInt32(component.Response.Status);
                Assert.True(httpStatusCode == HttpStatusCode.OK || httpStatusCode == HttpStatusCode.Created);

                recordIdentifiers.Add(new HealthRecordIdentifier(component.Resource.TypeName, component.Resource.Id));
            }

            return recordIdentifiers;
        }

        protected sealed class HealthRecordIdentifier
        {
            public HealthRecordIdentifier(string resourceType, string id)
            {
                ResourceType = resourceType;
                Id = id;
            }

            public string ResourceType { get; }

            public string Id { get; }
        }
    }
}
