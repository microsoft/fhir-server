// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceWrapperExtensions
    {
        private static readonly RecyclableMemoryStreamManager _memoryStreamManager = new RecyclableMemoryStreamManager();

        /// <summary>
        /// Get the raw data from resourceWrapper as a string and JsonDocument.
        /// If the RawResource's VersionSet or LastUpdatedSet are false, then the RawResource's data will be updated
        /// to have them set to the values in the ResourceWrapper
        /// </summary>
        /// <param name="resourceWrapper">Input ResourceWrapper to convert to a JsonDocument</param>
        /// <returns>A <see cref="JsonDocument"/></returns>
        public static string SerializeToJsonString(this ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            if (resourceWrapper.RawResource.MetaSet)
            {
                return resourceWrapper.RawResource.Data;
            }

            var jsonDocument = JsonDocument.Parse(resourceWrapper.RawResource.Data);

            using (var ms = new RecyclableMemoryStream(_memoryStreamManager))
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

                            bool versionIdFound = false, lastUpdatedFound = false;

                            foreach (var metaEntry in current.Value.EnumerateObject())
                            {
                                if (metaEntry.Name == "lastUpdated")
                                {
                                    writer.WriteString("lastUpdated", resourceWrapper.LastModified);
                                    lastUpdatedFound = true;
                                }
                                else if (metaEntry.Name == "versionId")
                                {
                                    writer.WriteString("versionId", resourceWrapper.Version);
                                    versionIdFound = true;
                                }
                                else
                                {
                                    metaEntry.WriteTo(writer);
                                }
                            }

                            if (!lastUpdatedFound)
                            {
                                writer.WriteString("lastUpdated", resourceWrapper.LastModified);
                            }

                            if (!versionIdFound)
                            {
                                writer.WriteString("versionId", resourceWrapper.Version);
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

                using (var sr = new StreamReader(ms))
                {
                    ms.Position = 0;
                    return sr.ReadToEnd();
                }
            }
        }
    }
}
