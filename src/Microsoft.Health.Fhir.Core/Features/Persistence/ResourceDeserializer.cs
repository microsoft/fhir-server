// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceDeserializer
    {
        private readonly IReadOnlyDictionary<FhirResourceFormat, Func<string, string, DateTimeOffset, ResourceElement>> _deserializers;

        public ResourceDeserializer(IReadOnlyDictionary<FhirResourceFormat, Func<string, string, DateTimeOffset, ResourceElement>> deserializers)
        {
            EnsureArg.IsNotNull(deserializers, nameof(deserializers));

            _deserializers = deserializers;
        }

        public ResourceDeserializer(params (FhirResourceFormat Format, Func<string, string, DateTimeOffset, ResourceElement> Func)[] deserializers)
        {
            EnsureArg.IsNotNull(deserializers, nameof(deserializers));

            _deserializers = deserializers.ToDictionary(x => x.Format, x => x.Func);
        }

        public ResourceElement Deserialize(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            ResourceElement resource = DeserializeRaw(resourceWrapper.RawResource, resourceWrapper.Version, resourceWrapper.LastModified);

            return resource;
        }

        /// <summary>
        /// Deserialize RawResource in ResourceWrapper to a JsonDocument.
        /// If the RawResource's VersionSet or LastUpdatedSet are false, then the RawResource's data will be updated
        /// to have them set to the values in the ResourceWrapper
        /// </summary>
        /// <param name="resourceWrapper">Input ResourceWrapper to convert to a JsonDocument</param>
        /// <returns>A <see cref="JsonDocument"/></returns>
        public static JsonDocument DeserializeToJsonDocument(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            if (resourceWrapper.RawResource.LastUpdatedSet && resourceWrapper.RawResource.VersionSet)
            {
                return JsonDocument.Parse(resourceWrapper.RawResource.Data);
            }

            var jsonDocument = JsonDocument.Parse(resourceWrapper.RawResource.Data);

            using (var ms = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
                {
                    writer.WriteStartObject();
                    bool foundMeta = false;

                    foreach (var current in jsonDocument.RootElement.EnumerateObject())
                    {
                        if (current.Name == "meta")
                        {
                            foundMeta = true;

                            writer.WriteStartObject("meta");

                            foreach (var metaEntry in current.Value.EnumerateObject())
                            {
                                if (metaEntry.Name == "lastUpdated")
                                {
                                    writer.WriteString("lastUpdated", resourceWrapper.LastModified);
                                }
                                else if (metaEntry.Name == "versionId")
                                {
                                    writer.WriteString("versionId", resourceWrapper.Version);
                                }
                                else
                                {
                                    metaEntry.WriteTo(writer);
                                }
                            }

                            writer.WriteEndObject();
                        }
                        else
                        {
                            // write
                            current.WriteTo(writer);
                        }
                    }

                    if (!foundMeta)
                    {
                        writer.WriteStartObject("meta");
                        writer.WriteString("lastUpdated", resourceWrapper.LastModified);
                        writer.WriteString("versionId", resourceWrapper.Version);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                ms.Position = 0;
                jsonDocument = JsonDocument.Parse(ms);

                using (var sr = new StreamReader(ms))
                {
                    ms.Position = 0;
                    resourceWrapper.RawResource.Data = sr.ReadToEnd();
                }
            }

            return jsonDocument;
        }

        internal ResourceElement DeserializeRaw(RawResource rawResource, string version, DateTimeOffset lastModified)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            if (!_deserializers.TryGetValue(rawResource.Format, out var deserializer))
            {
                throw new NotSupportedException();
            }

            return deserializer(rawResource.Data, version, lastModified);
        }
    }
}
