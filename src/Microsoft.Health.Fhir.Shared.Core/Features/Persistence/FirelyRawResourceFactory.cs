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
    /// Creates <see cref="RawResource"/> instances using Firely serialization only.
    /// </summary>
    public class FirelyRawResourceFactory : IRawResourceFactory
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirelyRawResourceFactory"/> class.
        /// </summary>
        /// <param name="fhirJsonSerializer">The Firely serializer used to write the resource.</param>
        public FirelyRawResourceFactory(FhirJsonSerializer fhirJsonSerializer)
        {
            _fhirJsonSerializer = EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
        }

        /// <inheritdoc />
        public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Resource>();

            poco.Meta ??= new Meta();
            string versionId = poco.Meta.VersionId;

            try
            {
                if (!keepMeta)
                {
                    poco.Meta.VersionId = null;
                }
                else if (!keepVersion)
                {
                    poco.Meta.VersionId = "1";
                }

                string json = _fhirJsonSerializer.SerializeToString(poco);
                return new RawResource(json, FhirResourceFormat.Json, keepMeta);
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
