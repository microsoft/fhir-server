// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Ignixa.Features.Persistence
{
    internal static class FirelyCompatibleResourceJsonWriter
    {
        public static string Write(ResourceJsonNode resource, IFhirSchemaProvider schema, string versionIdOverride, bool omitVersionId)
        {
            var root = (JsonObject)resource.ToSourceNavigator().Meta<JsonNode>();
            var writer = new Writer(schema, versionIdOverride, omitVersionId);
            writer.WriteObject(root, null, root: true);
            return writer.Output.ToString();
        }

        private sealed class Writer(IFhirSchemaProvider schema, string versionIdOverride, bool omitVersionId)
        {
            public StringBuilder Output { get; } = new();

            public void WriteObject(JsonObject node, IType type, bool root = false, bool resourceMeta = false, bool editVersion = false)
            {
                string resourceType = (node["resourceType"] as JsonValue)?.GetValue<string>();
                bool resource = resourceType is not null;
                if (resource)
                {
                    type = schema.GetTypeDefinition(resourceType);
                }

                int start = Output.Length;
                Output.Append('{');

                if (root)
                {
                    WriteProperty(node, type, start, "resourceType", node["resourceType"]);
                    WriteProperty(node, type, start, "id", node["id"]);
                    WriteMeta(start, node["meta"], editVersion: true);
                }

                bool replaceVersion = editVersion && versionIdOverride is not null;
                bool removeVersion = editVersion && omitVersionId;
                bool versionWritten = false;

                foreach ((string name, JsonNode value) in node)
                {
                    if ((root && name is "resourceType" or "id" or "meta")
                        || ((removeVersion || replaceVersion) && name == "_versionId")
                        || (removeVersion && name == "versionId"))
                    {
                        continue;
                    }

                    if (replaceVersion && name == "versionId")
                    {
                        WriteVersion(start);
                        versionWritten = true;
                    }
                    else if (resource && name == "meta")
                    {
                        WriteMeta(start, value, editVersion: false);
                    }
                    else if (resourceMeta && name == "lastUpdated" && value is JsonValue instant)
                    {
                        DateTimeOffset parsed = DateTimeOffset.Parse(instant.GetValue<string>(), CultureInfo.InvariantCulture);
                        WritePropertyName(name, start);
                        WriteString(parsed.ToInstantString());
                    }
                    else
                    {
                        WriteProperty(node, type, start, name, value);
                    }
                }

                if (replaceVersion && !versionWritten)
                {
                    WriteVersion(start);
                }

                Output.Append('}');
            }

            private void WriteMeta(int objectStart, JsonNode value, bool editVersion)
            {
                if (value is null)
                {
                    if (editVersion && versionIdOverride is not null)
                    {
                        WritePropertyName("meta", objectStart);
                        int metaStart = Output.Length;
                        Output.Append('{');
                        WriteVersion(metaStart);
                        Output.Append('}');
                    }

                    return;
                }

                int start = Output.Length;
                WritePropertyName("meta", objectStart);
                int valueStart = Output.Length;
                WriteObject((JsonObject)value, schema.GetTypeDefinition("Meta"), resourceMeta: true, editVersion: editVersion);
                if (Output.Length == valueStart + 2)
                {
                    Output.Length = start;
                }
            }

            private void WriteVersion(int objectStart)
            {
                WritePropertyName("versionId", objectStart);
                WriteString(versionIdOverride);
            }

            private void WriteProperty(JsonObject node, IType parent, int objectStart, string name, JsonNode value)
            {
                if (value is null)
                {
                    return;
                }

                bool companion = name.StartsWith("_", StringComparison.Ordinal);
                string elementName = companion ? name[1..] : name;
                (IType definition, IType type) = ResolveChild(parent, elementName);
                bool collection = definition?.IsCollection == true;
                bool primitive = type?.Info.IsPrimitive == true;
                IType valueType = companion ? schema.GetTypeDefinition("Element") : type;

                int start = Output.Length;
                WritePropertyName(name, objectStart);
                if (collection && primitive)
                {
                    JsonNode other = node[companion ? elementName : "_" + name];
                    if (!WritePrimitiveArray(value, other, valueType))
                    {
                        Output.Length = start;
                    }
                }
                else if (collection && value is not JsonArray)
                {
                    Output.Append('[');
                    if (WriteValue(value, valueType))
                    {
                        Output.Append(']');
                    }
                    else
                    {
                        Output.Length = start;
                    }
                }
                else if (!WriteValue(value, valueType))
                {
                    Output.Length = start;
                }
            }

            private bool WritePrimitiveArray(JsonNode value, JsonNode other, IType type)
            {
                int count = Math.Max(SlotCount(value), SlotCount(other));
                int lastPopulated = count;
                while (lastPopulated > 0 && !HasValue(Slot(value, lastPopulated - 1)) && !HasValue(Slot(other, lastPopulated - 1)))
                {
                    lastPopulated--;
                }

                Output.Append('[');
                bool containsValue = false;
                for (int i = 0; i < lastPopulated; i++)
                {
                    if (i > 0)
                    {
                        Output.Append(',');
                    }

                    if (WriteValue(Slot(value, i), type))
                    {
                        containsValue = true;
                    }
                    else
                    {
                        Output.Append("null");
                    }
                }

                Output.Append(']');
                return containsValue;
            }

            private bool WriteValue(JsonNode value, IType type)
            {
                int start = Output.Length;
                switch (value)
                {
                    case JsonObject obj:
                        WriteObject(obj, type);
                        break;
                    case JsonArray array:
                        Output.Append('[');
                        foreach (JsonNode item in array)
                        {
                            int itemStart = Output.Length;
                            if (itemStart > start + 1)
                            {
                                Output.Append(',');
                            }

                            if (!WriteValue(item, type))
                            {
                                Output.Length = itemStart;
                            }
                        }

                        Output.Append(']');
                        break;
                    case JsonValue scalar when scalar.TryGetValue<string>(out string text):
                        WriteString(text);
                        return true;
                    case JsonValue scalar:
                        Output.Append(scalar.ToJsonString());
                        return true;
                    default:
                        return false;
                }

                if (Output.Length > start + 2)
                {
                    return true;
                }

                Output.Length = start;
                return false;
            }

            private (IType Definition, IType Type) ResolveChild(IType parent, string name)
            {
                if (parent is null)
                {
                    return default;
                }

                foreach (IType child in parent.Children)
                {
                    if (child is not ITypeExtended metadata)
                    {
                        continue;
                    }

                    string childName = child.Info.Name;
                    if (child.Info.IsChoiceElement)
                    {
                        if (name.StartsWith(childName, StringComparison.Ordinal))
                        {
                            ReadOnlySpan<char> suffix = name.AsSpan(childName.Length);
                            foreach (ITypeReference candidate in metadata.Types)
                            {
                                string code = candidate.Code;
                                if (suffix.Length == code.Length && suffix[0] == char.ToUpperInvariant(code[0]) && suffix[1..].SequenceEqual(code.AsSpan(1)))
                                {
                                    return (child, schema.GetTypeDefinition(code));
                                }
                            }
                        }
                    }
                    else if (name == childName)
                    {
                        if (metadata.ContentReference is { Length: > 0 } reference)
                        {
                            return (child, schema.GetTypeDefinition(reference.TrimStart('#')));
                        }

                        string code = metadata.DefaultTypeName;
                        if (code is "BackboneElement" or "BackboneType" or "Element"
                            && schema.GetTypeDefinition($"{parent.Info.Name}.{childName}") is IType backbone)
                        {
                            return (child, backbone);
                        }

                        return (child, code is null ? null : schema.GetTypeDefinition(code));
                    }
                }

                return default;
            }

            private static int SlotCount(JsonNode node) => node is JsonArray array ? array.Count : node is null ? 0 : 1;

            private static JsonNode Slot(JsonNode node, int index) =>
                node is JsonArray array ? index < array.Count ? array[index] : null : index == 0 ? node : null;

            private static bool HasValue(JsonNode node)
            {
                switch (node)
                {
                    case JsonObject obj:
                        foreach ((_, JsonNode child) in obj)
                        {
                            if (HasValue(child))
                            {
                                return true;
                            }
                        }

                        return false;
                    case JsonArray array:
                        foreach (JsonNode item in array)
                        {
                            if (HasValue(item))
                            {
                                return true;
                            }
                        }

                        return false;
                    default:
                        return node is not null;
                }
            }

            private void WritePropertyName(string name, int objectStart)
            {
                if (Output.Length > objectStart + 1)
                {
                    Output.Append(',');
                }

                WriteString(name);
                Output.Append(':');
            }

            private void WriteString(string value)
            {
                Output.Append('"');
                foreach (Rune scalar in value.EnumerateRunes())
                {
                    string escape = scalar.Value switch
                    {
                        '"' => "\\\"",
                        '\\' => "\\\\",
                        '\b' => "\\b",
                        '\f' => "\\f",
                        '\n' => "\\n",
                        '\r' => "\\r",
                        '\t' => "\\t",
                        _ => null,
                    };
                    if (escape is not null)
                    {
                        Output.Append(escape);
                    }
                    else if (scalar.Value is < 0x20 or 0x85 or 0x2028 or 0x2029)
                    {
                        Output.Append("\\u").Append(scalar.Value.ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else if (scalar.IsBmp)
                    {
                        Output.Append((char)scalar.Value);
                    }
                    else
                    {
                        AppendSupplementary(scalar);
                    }
                }

                Output.Append('"');
            }

            private void AppendSupplementary(Rune scalar)
            {
                Span<char> pair = stackalloc char[2];
                scalar.EncodeToUtf16(pair);
                Output.Append(pair);
            }
        }
    }
}
