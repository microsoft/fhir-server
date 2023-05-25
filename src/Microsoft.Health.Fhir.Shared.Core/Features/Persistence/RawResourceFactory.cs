// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
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
        public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Resource>();

            poco.Meta = poco.Meta ?? new Meta();
            var versionId = poco.Meta.VersionId;

            try
            {
                // Clear meta version if keepMeta is false since this is set based on generated values when saving the resource
                if (!keepMeta)
                {
                    poco.Meta.VersionId = null;
                }
                else if (!keepVersion)
                {
                    // Assume it's 1, though it may get changed by the database.
                    poco.Meta.VersionId = "1";
                }

                return new RawResource(_fhirJsonSerializer.SerializeToString(poco), FhirResourceFormat.Json, keepMeta);
            }
            finally
            {
                if (!keepMeta)
                {
                    poco.Meta.VersionId = versionId;
                }
            }
        }
    }
}
