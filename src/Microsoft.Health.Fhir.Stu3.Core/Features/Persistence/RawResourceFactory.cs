// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Models;

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
        public RawResource Create(ResourceElement resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.Instance.ToPoco<Resource>();

            var versionId = poco.Meta?.VersionId;
            var lastUpdated = poco.Meta?.LastUpdated;

            try
            {
                // Clear meta version and lastUpdated since these are set based on generated values when saving the resource
                if (poco.Meta != null)
                {
                    poco.Meta.VersionId = null;
                    poco.Meta.LastUpdated = null;
                }

                return new RawResource(_fhirJsonSerializer.SerializeToString(poco), FhirResourceFormat.Json);
            }
            finally
            {
                if (poco.Meta != null)
                {
                    poco.Meta.VersionId = versionId;
                    poco.Meta.LastUpdated = lastUpdated;
                }
            }
        }
    }
}
