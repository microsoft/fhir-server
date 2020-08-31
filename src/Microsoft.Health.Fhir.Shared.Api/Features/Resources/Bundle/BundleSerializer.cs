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
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class BundleSerializer
    {
        private readonly JsonWriterOptions _writerOptions = new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private readonly RecyclableMemoryStreamManager _memoryStreamManager = new RecyclableMemoryStreamManager();

        private const string DateTimeOffsetFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK";

        public BundleSerializer()
        {
        }

        public async Task Serialize(Hl7.Fhir.Model.Bundle bundle, Stream outputStream)
        {
            using (RecyclableMemoryStream ms = new RecyclableMemoryStream(_memoryStreamManager))
            using (Utf8JsonWriter writer = new Utf8JsonWriter(ms, _writerOptions))
            using (StreamWriter streamWriter = new StreamWriter(ms))
            {
                writer.WriteStartObject();

                writer.WriteString("resourceType", bundle.ResourceType.GetLiteral());
                writer.WriteString("id", bundle.Id);

                SerializeMetadata();

                writer.WriteString("type", bundle.Type?.GetLiteral());

                SerializeLinks();

                if (bundle.Total.HasValue)
                {
                    writer.WriteNumber("total", bundle.Total.Value);
                }

                await SerializeEntries();

                writer.WriteEndObject();
                await writer.FlushAsync();

                ms.WriteTo(outputStream);

                void SerializeMetadata()
                {
                    if (bundle.Meta != null)
                    {
                        writer.WriteStartObject("meta");
                        writer.WriteString("lastUpdated", bundle.Meta?.LastUpdated?.ToString(DateTimeOffsetFormat));
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

                async Task SerializeEntries()
                {
                    if (bundle.Entry?.Any() == true)
                    {
                        writer.WriteStartArray("entry");
                        foreach (var entry in bundle.Entry)
                        {
                            var rawBundleEntry = entry as RawBundleEntryComponent;
                            writer.WriteStartObject();

                            if (!string.IsNullOrEmpty(rawBundleEntry.FullUrl))
                            {
                                writer.WriteString("fullUrl", rawBundleEntry.FullUrl);
                            }

                            writer.WritePropertyName("resource");
                            await writer.FlushAsync();

                            // The Utf8JsonWriter does not support inserting raw json. We can write a JsonDocument, but that involves an extra parse that should be unnecessary.
                            // Instead, we will track the current position after writing the resource property name, then write a null value, seek back to the position after
                            // the property name, and use a StreamWriter to write the raw json string in. The WriteNullValue call is necessary for the Utf8JsonWriter to maintain
                            // a valid state.
                            var currentStreamPosition = ms.Position;
                            writer.WriteNullValue();
                            await writer.FlushAsync();
                            ms.Seek(currentStreamPosition, SeekOrigin.Begin);
                            await streamWriter.WriteAsync(rawBundleEntry.ResourceElement.ResourceData);
                            streamWriter.Flush();

                            if (rawBundleEntry?.Search?.Mode != null)
                            {
                                writer.WriteStartObject("search");
                                writer.WriteString("mode", rawBundleEntry.Search?.Mode?.GetLiteral());
                                writer.WriteEndObject();
                            }

                            if (rawBundleEntry.Request != null)
                            {
                                writer.WriteStartObject("request");

                                writer.WriteString("method", rawBundleEntry.Request.Method.GetLiteral());
                                writer.WriteString("url", rawBundleEntry.Request.Url);

                                writer.WriteEndObject();
                            }

                            if (rawBundleEntry.Response != null)
                            {
                                writer.WriteStartObject("response");

                                writer.WriteString("etag", rawBundleEntry.Response.Etag);
                                writer.WriteString("lastModified", rawBundleEntry.Response.LastModified?.ToString(DateTimeOffsetFormat));

                                writer.WriteEndObject();
                            }

                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }
                }
            }
        }
    }
}
