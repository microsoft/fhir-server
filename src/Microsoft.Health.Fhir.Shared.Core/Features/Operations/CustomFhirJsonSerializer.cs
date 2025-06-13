// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class CustomFhirJsonSerializer<T> : ICustomFhirJsonSerializer<T>
        where T : Resource
    {
        public string Serialize(T fhirResource)
        {
            // Serialize the FHIR resoruce using the FHIR serializer
            var fhirSerializer = new FhirJsonSerializer();
            string payloadJsonString = fhirSerializer.SerializeToString(fhirResource);
            return payloadJsonString;
        }

        public T Deserialize(string resourceString)
        {
            // Parse the JSON
            var fhirJsonParser = new FhirJsonParser();
            T resource = fhirJsonParser.Parse<T>(resourceString);
            return resource;
        }
    }
}
