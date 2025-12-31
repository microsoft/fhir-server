// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides Ignixa <see cref="ISchema"/> instances based on the FHIR specification version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is designed to be registered as a singleton and provides cached schema providers
    /// for each FHIR version. The schema provider is determined at construction time based on
    /// the <see cref="IModelInfoProvider.Version"/> property.
    /// </para>
    /// <para>
    /// Currently supported FHIR versions:
    /// </para>
    /// <list type="bullet">
    /// <item><description>R4 - Uses <see cref="R4CoreSchemaProvider"/></description></item>
    /// <item><description>R5 - Uses <see cref="R5CoreSchemaProvider"/></description></item>
    /// </list>
    /// <para>
    /// STU3 and R4B support can be added when schema providers become available.
    /// </para>
    /// </remarks>
    public class IgnixaSchemaContext : IIgnixaSchemaContext
    {
        private readonly IFhirSchemaProvider _fhirSchemaProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaSchemaContext"/> class.
        /// </summary>
        /// <param name="modelInfoProvider">The model info provider to determine FHIR version.</param>
        /// <exception cref="NotSupportedException">
        /// Thrown when the FHIR version is not supported.
        /// </exception>
        public IgnixaSchemaContext(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _fhirSchemaProvider = CreateSchemaProvider(modelInfoProvider.Version);
        }

        /// <inheritdoc />
        public ISchema Schema => _fhirSchemaProvider;

        /// <inheritdoc />
        public IFhirSchemaProvider FhirSchemaProvider => _fhirSchemaProvider;

        /// <inheritdoc />
        public IReferenceMetadataProvider ReferenceMetadataProvider => _fhirSchemaProvider.ReferenceMetadataProvider;

        /// <summary>
        /// Creates the appropriate schema provider for the specified FHIR version.
        /// </summary>
        /// <param name="fhirVersion">The FHIR specification version.</param>
        /// <returns>The corresponding Ignixa schema provider.</returns>
        /// <exception cref="NotSupportedException">
        /// Thrown when the FHIR version is not supported.
        /// </exception>
        private static IFhirSchemaProvider CreateSchemaProvider(FhirSpecification fhirVersion)
        {
            return fhirVersion switch
            {
                // STU3 and R4B schema providers can be added when available in Ignixa.Specification.Generated
                FhirSpecification.Stu3 => new R4CoreSchemaProvider(), // Fallback to R4 for STU3 until Stu3CoreSchemaProvider is available
                FhirSpecification.R4 => new R4CoreSchemaProvider(),
                FhirSpecification.R4B => new R4CoreSchemaProvider(), // Fallback to R4 for R4B until R4BCoreSchemaProvider is available
                FhirSpecification.R5 => new R5CoreSchemaProvider(),
                _ => throw new NotSupportedException($"FHIR version {fhirVersion} is not supported for Ignixa schema context."),
            };
        }
    }
}
