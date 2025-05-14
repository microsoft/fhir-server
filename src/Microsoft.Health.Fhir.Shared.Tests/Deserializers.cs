// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class Deserializers
    {
        private static readonly FhirJsonDeserializer JsonParser = new FhirJsonDeserializer();

        public static ResourceDeserializer ResourceDeserializer => new ResourceDeserializer((FhirResourceFormat.Json, ConvertJson));

        private static ResourceElement ConvertJson(string str, string version, DateTimeOffset lastModified)
        {
            var resource = JsonParser.DeserializeResource(str);
            resource.VersionId = version;
            resource.Meta.LastUpdated = lastModified;
            return resource.ToPocoNode().ToResourceElement();
        }
    }
}
