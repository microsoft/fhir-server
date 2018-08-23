// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Web;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Xml
{
    [Trait(Traits.Category, Categories.Xml)]
    public class HistoryXmlTests : HistoryTests
    {
        public HistoryXmlTests(HttpIntegrationTestFixture<Startup> fixture)
            : base(fixture)
        {
            Client = fixture.FhirXmlClient.Value;
        }
    }
}
