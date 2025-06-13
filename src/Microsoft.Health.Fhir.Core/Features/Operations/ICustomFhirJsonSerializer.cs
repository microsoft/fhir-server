// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Serializer and deserializer for FHIR resources of type T.
    /// </summary>
    /// <typeparam name="T">A type that inherits from Hl7.Fhir.Model.Resource</typeparam>
    public interface ICustomFhirJsonSerializer<T>
        where T : Resource
    {
        /// <summary>
        /// Serializes a FHIR resource into a JSON string.
        /// </summary>
        /// <param name="fhirResource">The FHIR resource to serialize.</param>
        /// <returns>A JSON string representation of the resource.</returns>
        string Serialize(T fhirResource);

        /// <summary>
        /// Deserializes a JSON string into a FHIR resource.
        /// </summary>
        /// <param name="resourceString">The JSON string to deserialize.</param>
        /// <returns>The deserialized FHIR resource.</returns>
        T Deserialize(string resourceString);
    }
}
