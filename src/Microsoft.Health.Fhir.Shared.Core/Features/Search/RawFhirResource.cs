// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Text.Json;
using EnsureThat;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search
{
    [FhirType(IsResource = true)]
    public class RawFhirResource : Bundle.EntryComponent
    {
        public RawFhirResource(ResourceWrapper wrapper)
        {
            EnsureArg.IsNotNull(wrapper, nameof(wrapper));

            Wrapper = wrapper;
            SetContentWithMetadata();
        }

        public JsonDocument Content { get; set; }

        public ResourceWrapper Wrapper { get; set; }

        public override IDeepCopyable DeepCopy()
        {
            // TODO YAZAN
            return this;
        }

        private void SetContentWithMetadata()
        {
            if (Wrapper.RawResource.LastUpdatedSet && Wrapper.RawResource.VersionSet)
            {
                Content = JsonDocument.Parse(Wrapper.RawResource.Data);
                return;
            }

            var jsonDocument = JsonDocument.Parse(Wrapper.RawResource.Data);

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
                                    writer.WriteString("lastUpdated", Wrapper.LastModified);
                                }
                                else if (metaEntry.Name == "versionId")
                                {
                                    writer.WriteString("versionId", Wrapper.Version);
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
                        writer.WriteString("lastUpdated", Wrapper.LastModified);
                        writer.WriteString("versionId", Wrapper.Version);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                ms.Position = 0;
                Content = JsonDocument.Parse(ms);

                using (var sr = new StreamReader(ms))
                {
                    ms.Position = 0;
                    Wrapper.RawResource.Data = sr.ReadToEnd();
                }
            }
        }
    }
}
