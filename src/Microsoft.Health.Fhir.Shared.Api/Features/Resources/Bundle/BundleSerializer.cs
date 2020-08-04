// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public static class BundleSerializer
    {
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static async Task Serialize(Hl7.Fhir.Model.Bundle bundle, Stream outputStream)
        {
            using (Utf8JsonWriter writer = new Utf8JsonWriter(outputStream, WriterOptions))
            {
                writer.WriteStartObject();

                writer.WriteString("resourceType", bundle.ResourceType.GetLiteral());
                writer.WriteString("id", bundle.Id);

                SerializeMetadata();

                writer.WriteString("type", bundle.Type?.GetLiteral());

                SerializeLinks();
                SerializeEntries();

                if (bundle.Total.HasValue)
                {
                    writer.WriteNumber("total", bundle.Total.Value);
                }

                writer.WriteEndObject();
                await writer.FlushAsync();

                void SerializeMetadata()
                {
                    if (bundle.Meta != null)
                    {
                        writer.WriteStartObject("meta");
                        writer.WriteString("lastUpdated", bundle.Meta?.LastUpdated?.ToString("o"));
                        writer.WriteEndObject();
                    }
                }

                void SerializeLinks()
                {
                    if (bundle.Link?.Any() == true)
                    {
                        writer.WriteStartArray("link");

                        foreach (var link in bundle.Link)
                        {
                            writer.WriteStartObject();
                            writer.WritePropertyName("relation");
                            writer.WriteStringValue(link.Relation);
                            writer.WritePropertyName("url");
                            writer.WriteStringValue(link.Url);
                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }
                }

                void SerializeEntries()
                {
                    if (bundle.Entry?.Any() == true)
                    {
                        writer.WriteStartArray("entry");
                        foreach (var entry in bundle.Entry)
                        {
                            var rawBundleEntry = entry as RawBundleEntryComponent;
                            writer.WriteStartObject();
                            writer.WriteString("fullUrl", rawBundleEntry.FullUrl);
                            writer.WritePropertyName("resource");
                            rawBundleEntry.ResourceElement.JsonDocument.WriteTo(writer);
                            rawBundleEntry.ResourceElement.JsonDocument.Dispose();

                            writer.WriteStartObject("search");
                            writer.WriteString("mode", rawBundleEntry.Search?.Mode?.GetLiteral());

                            writer.WriteEndObject();
                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }
                }
            }
        }
    }
}
