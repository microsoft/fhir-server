// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// A serializer used to serialize the resource represented by <see cref="ResourceElement"/> to byte array.
    /// </summary>
    public interface IResourceToByteArraySerializer
    {
        /// <summary>
        /// Serializes the resource represented by <see cref="ResourceElement"/> to byte array.
        /// </summary>
        /// <param name="resourceElement">The resource element used to serialize.</param>
        /// <returns>The serialized bytes.</returns>
        byte[] Serialize(ResourceElement resourceElement);

        string StringSerialize(ResourceElement resourceElement);
    }
}
