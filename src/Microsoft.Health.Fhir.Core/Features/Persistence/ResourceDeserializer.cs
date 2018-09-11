// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public static class ResourceDeserializer
    {
        private static readonly FhirXmlParser FhirXmlParser = new FhirXmlParser();
        private static readonly FhirJsonParser FhirJsonParser = new FhirJsonParser();

        public static Resource Deserialize(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            Resource resource = DeserializeRaw(resourceWrapper.RawResource);

            resource.VersionId = resourceWrapper.Version;
            resource.Meta.LastUpdated = resourceWrapper.LastModified;

            return resource;
        }

        internal static Resource DeserializeRaw(RawResource rawResource)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            Resource resource;

            switch (rawResource.Format)
            {
                case ResourceFormat.Xml:
                    resource = FhirXmlParser.Parse<Resource>(rawResource.Data);
                    break;
                case ResourceFormat.Json:
                    resource = FhirJsonParser.Parse<Resource>(rawResource.Data);
                    break;
                default:
                    throw new NotSupportedException();
            }

            return resource;
        }
    }
}
