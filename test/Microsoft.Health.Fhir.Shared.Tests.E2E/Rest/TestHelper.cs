// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using Hl7.Fhir.Model;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    internal static class TestHelper
    {
        internal static void AssertLocationHeaderIsCorrect(FhirClient fhirClient, Resource createdResource, Uri location)
        {
            Assert.Equal($"{fhirClient.HttpClient.BaseAddress}Observation/{createdResource.Id}/_history/{createdResource.Meta.VersionId}", location.OriginalString);
        }

        internal static void AssertLastUpdatedAndLastModifiedAreEqual(DateTimeOffset? lastUpdated, DateTimeOffset? lastModified)
        {
            // The last modified header is stored in a format that does not include milliseconds
            Assert.Equal(lastUpdated.Value.Date, lastModified.Value.Date);
            Assert.Equal(lastUpdated.Value.Hour, lastModified.Value.Hour);
            Assert.Equal(lastUpdated.Value.Minute, lastModified.Value.Minute);
            Assert.Equal(lastUpdated.Value.Second, lastModified.Value.Second);
        }

        internal static void AssertSecurityHeaders(HttpResponseHeaders headers)
        {
            Assert.True(headers.TryGetValues("X-Content-Type-Options", out IEnumerable<string> headerValue));
            Assert.Contains("nosniff", headerValue);
        }
    }
}
