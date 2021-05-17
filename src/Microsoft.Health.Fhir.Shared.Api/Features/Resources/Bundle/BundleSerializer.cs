// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class BundleSerializer
    {
        private readonly JsonWriterOptions _writerOptions = new JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly JsonWriterOptions _indentedWriterOptions = new JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = true,
        };

        public BundleSerializer()
        {
        }

        public async Task Serialize(Hl7.Fhir.Model.Bundle bundle, Stream outputStream, bool pretty = false)
        {
            await using Utf8JsonWriter writer = new Utf8JsonWriter(outputStream, pretty ? _indentedWriterOptions : _writerOptions);
            await using StreamWriter streamWriter = new StreamWriter(outputStream, leaveOpen: true);

            writer.WriteStartObject();

            writer.WriteString("resourceType", bundle.TypeName);
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

            void SerializeMetadata()
            {
                if (bundle.Meta != null)
                {
                    writer.WriteStartObject("meta");
                    writer.WriteString("lastUpdated", bundle.Meta?.LastUpdated?.ToInstantString());
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
                        if (!(entry is RawBundleEntryComponent rawBundleEntry))
                        {
                            throw new ArgumentException("BundleSerializer can only be used when all Entry elements are of type RawBundleEntryComponent.", nameof(bundle));
                        }

                        bool wroteFullUrl = false;
                        writer.WriteStartObject();

                        if (!string.IsNullOrEmpty(rawBundleEntry.FullUrl))
                        {
                            writer.WriteString("fullUrl", rawBundleEntry.FullUrl);
                            await writer.FlushAsync();
                            await streamWriter.WriteAsync(",");
                            wroteFullUrl = true;
                        }

                        await writer.FlushAsync();
                        await streamWriter.WriteAsync("\"resource\":");
                        await streamWriter.FlushAsync();

                        await rawBundleEntry.ResourceElement.SerializeToStreamAsUtf8Json(outputStream);

                        if (!wroteFullUrl && (rawBundleEntry?.Search?.Mode != null || rawBundleEntry.Request != null || rawBundleEntry.Response != null))
                        {
                            // If fullUrl was written, the Utf8JsonWriter knows it needs to write a comma before the next property since a comma is needed, and will do so.
                            // If fullUrl wasn't written, since we are writing resource in a separate writer, we need to add this comma manually.
                            await streamWriter.WriteAsync(",");
                            await streamWriter.FlushAsync();
                        }

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
                            writer.WriteString("lastModified", rawBundleEntry.Response.LastModified?.ToInstantString());

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
