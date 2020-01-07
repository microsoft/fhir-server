// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Transaction)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.All)]
    public class TransactionTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public TransactionTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithVersionedReference_WhenSubmittingATransaction_ThenResolvedReferenceIsVersionSpecific()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithVersionSpecificReference").ToPoco<Bundle>();

            var fullUrlBeforeTransaction = requestBundle.Entry[0].FullUrl;
            var referenceBeforeTransaction = requestBundle.Entry[1].Resource.GetAllChildren<ResourceReference>().ToList()[0].Reference;

            Assert.True(referenceBeforeTransaction.Contains(fullUrlBeforeTransaction, StringComparison.OrdinalIgnoreCase));
            Assert.True(referenceBeforeTransaction.Contains("/_history/1", StringComparison.OrdinalIgnoreCase));

            FhirResponse<Bundle> fhirResponse = await Client.PostBundleAsync(requestBundle);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            var idAfterTransaction = fhirResponse.Resource.Entry[0].Resource.Id;

            var resolvedReferencesAfterTransaction = fhirResponse.Resource.Entry[1].Resource.GetAllChildren<ResourceReference>().ToList()[0].Reference;

            Assert.True(resolvedReferencesAfterTransaction.Contains(idAfterTransaction, StringComparison.OrdinalIgnoreCase));
            Assert.True(resolvedReferencesAfterTransaction.Contains("/_history/1", StringComparison.OrdinalIgnoreCase));
        }
    }
}
