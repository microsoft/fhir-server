using System.Text.Encodings.Web;
using System.Text.Json;
using REDox;

namespace RedoxExperiment;

internal static class MetadataRewrite
{
    private const string Version = "2";
    private const string LastUpdated = "2026-01-02T03:04:05.123Z";

    public static byte[] Rewrite(string json, string variant)
    {
        if (variant == "redox")
        {
            using var document = REDox.Json.JsonDocument.Parse(json,
                options: new REDox.Json.JsonDocumentOptions { EnableValueValidation = true, MaxDepth = 64 });
            var root = document.RootElement.AsObject();
            if (document.RootElement.TryGetProperty("meta", out var existing))
            {
                var meta = existing.AsObject();
                meta["versionId"] = Version;
                meta["lastUpdated"] = LastUpdated;
            }
            else
            {
                var meta = new DObject
                {
                    ["versionId"] = Version,
                    ["lastUpdated"] = LastUpdated,
                };
                root["meta"] = meta;
            }

            return REDox.Json.JsonDocument.Encode(document.RootElement);
        }

        using var baseline = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            var foundMeta = false;
            foreach (var property in baseline.RootElement.EnumerateObject())
            {
                if (!property.NameEquals("meta"))
                {
                    property.WriteTo(writer);
                    continue;
                }

                foundMeta = true;
                writer.WriteStartObject("meta");
                var foundVersion = false;
                var foundLastUpdated = false;
                foreach (var meta in property.Value.EnumerateObject())
                {
                    if (meta.NameEquals("versionId"))
                    {
                        writer.WriteString("versionId", Version);
                        foundVersion = true;
                    }
                    else if (meta.NameEquals("lastUpdated"))
                    {
                        writer.WriteString("lastUpdated", LastUpdated);
                        foundLastUpdated = true;
                    }
                    else
                    {
                        meta.WriteTo(writer);
                    }
                }

                if (!foundVersion)
                {
                    writer.WriteString("versionId", Version);
                }

                if (!foundLastUpdated)
                {
                    writer.WriteString("lastUpdated", LastUpdated);
                }

                writer.WriteEndObject();
            }

            if (!foundMeta)
            {
                writer.WriteStartObject("meta");
                writer.WriteString("versionId", Version);
                writer.WriteString("lastUpdated", LastUpdated);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
