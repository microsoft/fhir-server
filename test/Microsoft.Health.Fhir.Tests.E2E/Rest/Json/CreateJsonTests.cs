// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Json
{
    public class CreateJsonTests : CreateTests<HttpIntegrationTestFixture<Startup>>
    {
        public CreateJsonTests(HttpIntegrationTestFixture<Startup> fixture)
            : base(fixture)
        {
        }
    }
}
