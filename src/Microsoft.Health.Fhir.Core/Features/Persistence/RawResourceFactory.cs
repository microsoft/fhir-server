// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="RawResource"/>
    /// </summary>
    public class RawResourceFactory : IRawResourceFactory
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="RawResourceFactory"/> class.
        /// </summary>
        /// <param name="fhirJsonSerializer">The FhirJsonSerializer to use for serializing the resource.</param>
        public RawResourceFactory(FhirJsonSerializer fhirJsonSerializer)
        {
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));

            _fhirJsonSerializer = fhirJsonSerializer;
        }

        /// <inheritdoc />
        public RawResource Create(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var versionId = resource.Meta?.VersionId;
            var lastUpdated = resource.Meta?.LastUpdated;

            try
            {
                // Clear meta version and lastUpdated since these are set based on generated values when saving the resource
                if (resource.Meta != null)
                {
                    resource.Meta.VersionId = null;
                    resource.Meta.LastUpdated = null;
                }

                return new RawResource(_fhirJsonSerializer.SerializeToString(resource), ResourceFormat.Json);
            }
            finally
            {
                if (resource.Meta != null)
                {
                    resource.Meta.VersionId = versionId;
                    resource.Meta.LastUpdated = lastUpdated;
                }
            }
        }
    }
}
