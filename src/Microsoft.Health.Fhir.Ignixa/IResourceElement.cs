// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Ignixa;

/// <summary>
/// Common interface for FHIR resource wrappers providing access to core resource properties.
/// </summary>
/// <remarks>
/// This interface provides a consistent abstraction over different resource representations
/// (e.g., Ignixa's ResourceJsonNode vs Firely's Resource). It enables code to work with
/// resources regardless of the underlying serialization mechanism.
/// </remarks>
public interface IResourceElement
{
    /// <summary>
    /// Gets the FHIR resource type name (e.g., "Patient", "Observation").
    /// </summary>
    string InstanceType { get; }

    /// <summary>
    /// Gets the logical resource identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the resource version identifier.
    /// </summary>
    string VersionId { get; }

    /// <summary>
    /// Gets the timestamp when the resource was last updated.
    /// </summary>
    DateTimeOffset? LastUpdated { get; }

    /// <summary>
    /// Gets a value indicating whether this is a domain resource
    /// (not Bundle, Parameters, or Binary).
    /// </summary>
    bool IsDomainResource { get; }
}
