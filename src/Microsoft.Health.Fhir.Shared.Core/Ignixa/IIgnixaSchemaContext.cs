// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides access to Ignixa schema providers for the current FHIR version.
    /// </summary>
    /// <remarks>
    /// The schema context is used to obtain type metadata and validation rules
    /// for FHIR resources based on the server's configured FHIR version.
    /// </remarks>
    public interface IIgnixaSchemaContext
    {
        /// <summary>
        /// Gets the Ignixa schema provider for the current FHIR version.
        /// </summary>
        /// <remarks>
        /// The schema provides type definitions, element metadata, and cardinality
        /// information for all FHIR resource types in the configured version.
        /// </remarks>
        ISchema Schema { get; }

        /// <summary>
        /// Gets the FHIR schema provider with rich metadata for the current FHIR version.
        /// </summary>
        /// <remarks>
        /// Provides access to reference metadata, value set definitions, and other
        /// FHIR-specific schema information beyond basic type definitions.
        /// </remarks>
        IFhirSchemaProvider FhirSchemaProvider { get; }

        /// <summary>
        /// Gets the reference metadata provider for the current FHIR version.
        /// </summary>
        /// <remarks>
        /// Provides metadata about reference fields in FHIR resources, including
        /// element paths, cardinality, and target resource types.
        /// </remarks>
        IReferenceMetadataProvider ReferenceMetadataProvider { get; }
    }
}
