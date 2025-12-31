// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="RawResource"/> from Ignixa's <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <remarks>
    /// This factory serializes Ignixa resource nodes directly to JSON without converting to Firely POCOs,
    /// providing improved serialization performance for resources already in Ignixa format.
    /// </remarks>
    public interface IIgnixaRawResourceFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="RawResource"/> from an Ignixa <see cref="ResourceJsonNode"/>.
        /// </summary>
        /// <param name="resource">The Ignixa resource node to be serialized.</param>
        /// <param name="keepMeta">
        /// If true, preserves the meta section in the serialized output.
        /// If false, clears the meta.versionId before serialization.
        /// </param>
        /// <param name="keepVersion">
        /// If true, keeps the existing version id.
        /// If false and keepMeta is true, resets version to "1".
        /// </param>
        /// <returns>An instance of <see cref="RawResource"/> containing the serialized JSON.</returns>
        RawResource Create(ResourceJsonNode resource, bool keepMeta, bool keepVersion = false);
    }
}
