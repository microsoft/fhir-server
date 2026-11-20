// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Ignixa.Features.Persistence
{
    internal static class FirelyCompatibleResourceJsonWriter
    {
        private static readonly Regex LastUpdatedPattern = new(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}(?:T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?:Z|[+-][0-9]{2}:[0-9]{2}))?\z", RegexOptions.CultureInvariant);
        private static readonly Regex DecimalPattern = new(@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant);

        public static string Write(ResourceJsonNode resource, IFhirSchemaProvider schema, string versionIdOverride, bool omitVersionId)
        {
            var root = (JsonObject)resource.ToSourceNavigator().Meta<JsonNode>();
            var writer = new Writer(schema, versionIdOverride, omitVersionId);
            writer.WriteObject(root, null, null, root: true);
            return writer.Output.ToString();
        }

        private sealed class Writer(IFhirSchemaProvider schema, string versionIdOverride, bool omitVersionId)
        {
            public StringBuilder Output { get; } = new();

            public void WriteObject(JsonObject node, IType type, string path, bool root = false, bool editVersion = false)
            {
                bool resource = root || type.Info.Name is "Resource" or "DomainResource";
                string resourceType = null;
                if (resource)
                {
                    if (node["resourceType"] is not JsonValue discriminator
                        || !discriminator.TryGetValue(out resourceType)
                        || string.IsNullOrEmpty(resourceType))
                    {
                        throw Invalid(path is null ? "resourceType" : $"{path}.resourceType", "Expected a resource type name");
                    }

                    path ??= resourceType;
                    type = schema.GetTypeDefinition(resourceType);
                    if (type is null || !type.Info.IsResource || type.Info.IsAbstract)
                    {
                        throw Invalid($"{path}.resourceType", $"Unknown resource type '{resourceType}'");
                    }
                }

                int start = Output.Length;
                Output.Append('{');

                if (root)
                {
                    WritePropertyName("resourceType", start);
                    WriteString(resourceType);
                    WriteProperty(node, type, start, "id", node["id"], $"{path}.id");
                    WriteMeta(start, node["meta"], $"{path}.meta", editVersion: true);
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
                    else if (resource && name == "resourceType")
                    {
                        WritePropertyName(name, start);
                        WriteString(resourceType);
                    }
                    else if (resource && name == "meta")
                    {
                        WriteMeta(start, value, $"{path}.meta", editVersion: false);
                    }
                    else if (type.Info.Name == "Meta" && name == "lastUpdated")
                    {
                        string instantPath = $"{path}.lastUpdated";
                        if (value is not JsonValue instant
                            || !instant.TryGetValue<string>(out string text)
                            || !LastUpdatedPattern.IsMatch(text)
                            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
                        {
                            throw Invalid(instantPath, "Expected a FHIR instant or a full date");
                        }

                        WritePropertyName(name, start);
                        WriteString(parsed.ToInstantString());
                    }
                    else
                    {
                        WriteProperty(node, type, start, name, value, $"{path}.{name}");
                    }
                }

                if (replaceVersion && !versionWritten)
                {
                    WriteVersion(start);
                }

                Output.Append('}');
            }

            private void WriteMeta(int objectStart, JsonNode value, string path, bool editVersion)
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

                if (value is not JsonObject meta)
                {
                    throw Invalid(path, "Expected a Meta object");
                }

                int start = Output.Length;
                WritePropertyName("meta", objectStart);
                int valueStart = Output.Length;
                WriteObject(meta, schema.GetTypeDefinition("Meta"), path, editVersion: editVersion);
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

            private void WriteProperty(JsonObject node, IType parent, int objectStart, string name, JsonNode value, string path)
            {
                bool companion = name.StartsWith("_", StringComparison.Ordinal);
                string elementName = companion ? name[1..] : name;
                (IType definition, IType type) = ResolveChild(parent, elementName);
                if (type is null || (companion && !type.Info.IsPrimitive))
                {
                    throw Invalid(path, "Unknown element");
                }

                if (value is null)
                {
                    return;
                }

                bool collection = definition.IsCollection;
                IType valueType = companion ? schema.GetTypeDefinition("Element") : type;

                int start = Output.Length;
                WritePropertyName(name, objectStart);
                if (collection && type.Info.IsPrimitive)
                {
                    JsonNode other = node[companion ? elementName : "_" + name];
                    if (!WritePrimitiveArray(value, other, valueType, path))
                    {
                        Output.Length = start;
                    }
                }
                else if (collection)
                {
                    int arrayStart = Output.Length;
                    Output.Append('[');
                    for (int i = 0; i < SlotCount(value); i++)
                    {
                        int itemStart = Output.Length;
                        if (itemStart > arrayStart + 1)
                        {
                            Output.Append(',');
                        }

                        if (!WriteValue(Slot(value, i), valueType, $"{path}[{i}]"))
                        {
                            Output.Length = itemStart;
                        }
                    }

                    if (Output.Length > arrayStart + 1)
                    {
                        Output.Append(']');
                    }
                    else
                    {
                        Output.Length = start;
                    }
                }
                else if (!WriteValue(value, valueType, path))
                {
                    Output.Length = start;
                }
            }

            private bool WritePrimitiveArray(JsonNode value, JsonNode other, IType type, string path)
            {
                int count = Math.Max(SlotCount(value), SlotCount(other));
                Output.Append('[');
                int end = Output.Length;
                bool containsValue = false;
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                    {
                        Output.Append(',');
                    }

                    if (WriteValue(Slot(value, i), type, $"{path}[{i}]"))
                    {
                        containsValue = true;
                        end = Output.Length;
                    }
                    else
                    {
                        Output.Append("null");
                        if (HasValue(Slot(other, i)))
                        {
                            end = Output.Length;
                        }
                    }
                }

                Output.Length = end;
                Output.Append(']');
                return containsValue;
            }

            private bool WriteValue(JsonNode value, IType type, string path)
            {
                if (value is null)
                {
                    return false;
                }

                if (type.Info.IsPrimitive || type.Info.Name == "xhtml")
                {
                    if (value is not JsonValue scalar)
                    {
                        throw Invalid(path, $"Expected a {type.Info.Name} primitive");
                    }

                    WritePrimitive(scalar, type.Info.Name, path);
                    return true;
                }

                if (value is not JsonObject obj)
                {
                    throw Invalid(path, $"Expected a {type.Info.Name} object");
                }

                int start = Output.Length;
                WriteObject(obj, type, path);
                if (Output.Length > start + 2)
                {
                    return true;
                }

                Output.Length = start;
                return false;
            }

            private void WritePrimitive(JsonValue value, string type, string path)
            {
                bool isString = value.TryGetValue<string>(out string literal);
                string text = isString ? literal : value.ToJsonString();
                switch (type)
                {
                    case "boolean" when text is "true" or "false":
                        Output.Append(text);
                        return;
                    case "integer":
                    case "positiveInt":
                    case "unsignedInt":
                        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)
                            && (type != "positiveInt" || number > 0)
                            && (type != "unsignedInt" || number >= 0))
                        {
                            Output.Append(number.ToString(CultureInfo.InvariantCulture));
                            return;
                        }

                        break;
                    case "integer64" when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long wideNumber):
                        WriteString(wideNumber.ToString(CultureInfo.InvariantCulture));
                        return;
                    case "decimal" when DecimalPattern.IsMatch(text)
                        && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                        Output.Append(text);
                        return;
                    case "boolean":
                    case "integer64":
                    case "decimal":
                    case "xhtml" when !isString:
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            WriteString(text);
                            return;
                        }

                        break;
                }

                throw Invalid(path, $"Invalid {type} primitive");
            }

            private static FormatException Invalid(string path, string message) => new($"{message} at '{path}'.");

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
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    string escape = character switch
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
                    else if (character is < '\u0020' or '\u0085' or '\u2028' or '\u2029')
                    {
                        Output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else if (char.IsSurrogate(character))
                    {
                        if (i + 1 == value.Length || !char.IsSurrogatePair(character, value[i + 1]))
                        {
                            throw new FormatException("Cannot write an unpaired UTF-16 surrogate to JSON.");
                        }

                        Output.Append(character).Append(value[++i]);
                    }
                    else
                    {
                        Output.Append(character);
                    }
                }

                Output.Append('"');
            }
        }
    }
}
