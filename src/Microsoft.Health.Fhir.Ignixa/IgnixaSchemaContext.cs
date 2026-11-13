// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa
{
    /// <summary>
    /// Provides access to the Ignixa generated FHIR schema for the FHIR version reported by an
    /// <see cref="IModelInfoProvider"/>.
    /// </summary>
    public sealed class IgnixaSchemaContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaSchemaContext"/> class.
        /// </summary>
        /// <param name="modelInfoProvider">The model info provider used to determine the active FHIR version.</param>
        public IgnixaSchemaContext(IModelInfoProvider modelInfoProvider)
        {
            ArgumentNullException.ThrowIfNull(modelInfoProvider);

            Schema = GetSchemaProvider(modelInfoProvider.Version);
        }

        /// <summary>
        /// Gets the generated Ignixa schema for the current FHIR version, including reference metadata.
        /// </summary>
        public IFhirSchemaProvider Schema { get; }

        private static IFhirSchemaProvider GetSchemaProvider(FhirSpecification version)
        {
            return version switch
            {
                FhirSpecification.Stu3 => new STU3CoreSchemaProvider(),
                FhirSpecification.R4 => new R4CoreSchemaProvider(),
                FhirSpecification.R4B => new R4BCoreSchemaProvider(),
                FhirSpecification.R5 => new R5CoreSchemaProvider(),
                _ => throw new NotSupportedException($"FHIR version '{version}' is not supported by the Ignixa schema provider."),
            };
        }
    }
}
