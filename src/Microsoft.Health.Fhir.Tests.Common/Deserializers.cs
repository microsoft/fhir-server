// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class Deserializers
    {
        private static readonly FhirJsonParser JsonParser = new FhirJsonParser(DefaultParserSettings.Settings);

        public static ResourceDeserializer ResourceDeserializer => new ResourceDeserializer(
            (ResourceFormat.Json, str => JsonParser.Parse<Resource>(str)));
    }
}
