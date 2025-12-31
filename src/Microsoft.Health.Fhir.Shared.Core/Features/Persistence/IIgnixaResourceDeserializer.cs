// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides deserialization of stored FHIR resources into Ignixa's <see cref="IgnixaResourceElement"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deserializer creates <see cref="IgnixaResourceElement"/> instances from stored JSON,
    /// which provides access to both the mutable <see cref="Ignixa.Serialization.SourceNodes.ResourceJsonNode"/>
    /// and schema-aware <see cref="Ignixa.Abstractions.IElement"/> views.
    /// </para>
    /// <para>
    /// Use this deserializer when you need to:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Apply in-place mutations (e.g., FHIR Patch)</description></item>
    /// <item><description>Perform search indexing with Ignixa's schema-aware navigation</description></item>
    /// <item><description>Avoid conversion to Firely POCOs for performance</description></item>
    /// </list>
    /// </remarks>
    public interface IIgnixaResourceDeserializer
    {
        /// <summary>
        /// Deserializes a <see cref="ResourceWrapper"/> into an <see cref="IgnixaResourceElement"/>.
        /// </summary>
        /// <param name="resourceWrapper">The resource wrapper containing the raw resource data.</param>
        /// <returns>An <see cref="IgnixaResourceElement"/> with appropriate meta information set.</returns>
        /// <exception cref="System.NotSupportedException">
        /// Thrown when the resource format is not JSON.
        /// </exception>
        IgnixaResourceElement Deserialize(ResourceWrapper resourceWrapper);

        /// <summary>
        /// Deserializes a <see cref="RawResourceElement"/> into an <see cref="IgnixaResourceElement"/>.
        /// </summary>
        /// <param name="rawResourceElement">The raw resource element containing the resource data.</param>
        /// <returns>An <see cref="IgnixaResourceElement"/> with appropriate meta information set.</returns>
        /// <exception cref="System.NotSupportedException">
        /// Thrown when the resource format is not JSON.
        /// </exception>
        IgnixaResourceElement Deserialize(RawResourceElement rawResourceElement);

        /// <summary>
        /// Deserializes a raw JSON string into an <see cref="IgnixaResourceElement"/>.
        /// </summary>
        /// <param name="json">The JSON string representing the FHIR resource.</param>
        /// <param name="version">The version identifier to set on the resource's meta.</param>
        /// <param name="lastModified">The last modified timestamp to set on the resource's meta.</param>
        /// <returns>An <see cref="IgnixaResourceElement"/> with the specified meta information.</returns>
        IgnixaResourceElement Deserialize(string json, string version, DateTimeOffset lastModified);
    }
}
