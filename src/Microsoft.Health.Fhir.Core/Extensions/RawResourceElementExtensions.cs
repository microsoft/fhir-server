// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class RawResourceElementExtensions
    {
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>
        /// Get the raw data from resourceWrapper as a string and JsonDocument.
        /// If the RawResource's VersionSet or LastUpdatedSet are false, then the RawResource's data will be updated
        /// to have them set to the values in the ResourceWrapper
        /// </summary>
        /// <param name="rawResource">Input RawResourceElement to convert to a JsonDocument</param>
        /// <param name="outputStream">Stream to serialize to</param>
        public static async Task SerializeToStreamAsJson(this RawResourceElement rawResource, Stream outputStream)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            if (rawResource.RawResource.IsMetaSet)
            {
                using (var sw = new StreamWriter(outputStream, leaveOpen: true))
                {
                    await sw.WriteAsync(rawResource.RawResource.Data);
                    await sw.FlushAsync();
                    return;
                }
            }

            var jsonDocument = JsonDocument.Parse(rawResource.RawResource.Data);

            using (Utf8JsonWriter writer = new Utf8JsonWriter(outputStream, WriterOptions))
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
                            if (metaEntry.Name == "lastUpdated" && rawResource.LastUpdated.HasValue)
                            {
                                string toWrite = rawResource.LastUpdated.HasValue ? rawResource.LastUpdated.Value.ToInstantString() : metaEntry.Value.GetString();
                                writer.WriteString("lastUpdated", toWrite);
                                lastUpdatedFound = true;
                            }
                            else if (metaEntry.Name == "versionId")
                            {
                                writer.WriteString("versionId", rawResource.VersionId);
                                versionIdFound = true;
                            }
                            else
                            {
                                metaEntry.WriteTo(writer);
                            }
                        }

                        if (!lastUpdatedFound && rawResource.LastUpdated.HasValue)
                        {
                            writer.WriteString("lastUpdated", rawResource.LastUpdated.Value.ToInstantString());
                        }

                        if (!versionIdFound)
                        {
                            writer.WriteString("versionId", rawResource.VersionId);
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
                    writer.WriteString("lastUpdated", rawResource.LastUpdated.Value.ToInstantString());
                    writer.WriteString("versionId", rawResource.VersionId);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();

                await writer.FlushAsync();
            }
        }
    }
}
