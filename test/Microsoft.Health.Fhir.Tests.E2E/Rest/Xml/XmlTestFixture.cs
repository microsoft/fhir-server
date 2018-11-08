// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Rest;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Xml
{
    public class XmlTestFixture<TStartup> : HttpIntegrationTestFixture<TStartup>
    {
        private FhirClient _fhirClient;

        public override FhirClient FhirClient
            => _fhirClient ?? (_fhirClient = new FhirClient(HttpClient, ResourceFormat.Xml));
    }
}
