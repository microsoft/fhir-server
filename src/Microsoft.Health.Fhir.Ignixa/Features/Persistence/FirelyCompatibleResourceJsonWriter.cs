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
                    if ((root && name is "resourceType" or "id" or "_id" or "meta")
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
                (ITypeExtended definition, IType type) = ResolveChild(parent, elementName);
                if (type is null || (companion && !IsPrimitive(type)))
                {
                    throw Invalid(path, "Unknown element");
                }

                if (IsPrimitive(type))
                {
                    if (!companion || !node.ContainsKey(elementName))
                    {
                        bool normalizeInstant = parent.Info.Name == "Meta" && elementName == "lastUpdated";
                        if (normalizeInstant && !companion && value is null)
                        {
                            throw Invalid(path, "Expected a FHIR instant or a full date");
                        }

                        WritePrimitiveProperty(objectStart, elementName, companion ? null : value, companion ? value : node["_" + name], definition, type.Info.Name, path, normalizeInstant);
                    }

                    return;
                }

                if (value is null)
                {
                    return;
                }

                int start = Output.Length;
                WritePropertyName(name, objectStart);
                if (definition.IsCollection)
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

                        if (!WriteComplexValue(Slot(value, i), type, $"{path}[{i}]"))
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
                else if (!WriteComplexValue(value, type, path))
                {
                    Output.Length = start;
                }
            }

            private void WritePrimitiveProperty(int objectStart, string name, JsonNode value, JsonNode companion, ITypeExtended definition, string type, string path, bool normalizeInstant)
            {
                string companionName = "_" + name;
                string companionPath = path[..(path.LastIndexOf('.', StringComparison.Ordinal) + 1)] + companionName;
                bool rejectEmpty = type == "code" && definition.Binding?.Strength == "Required";
                if (definition.IsCollection)
                {
                    WritePrimitiveArray(objectStart, name, value, companion, type, path, companionPath, rejectEmpty, writeCompanion: false);
                    WritePrimitiveArray(objectStart, companionName, value, companion, type, path, companionPath, rejectEmpty, writeCompanion: true);
                    return;
                }

                JsonObject metadata = PrimitiveMetadata(value, companion, path, companionPath);
                if (value is not null and not JsonObject)
                {
                    WritePropertyName(name, objectStart);
                    WritePrimitiveValue(value, type, path, rejectEmpty, normalizeInstant);
                }

                if (metadata is not null)
                {
                    int start = Output.Length;
                    WritePropertyName(companionName, objectStart);
                    if (!WriteComplexValue(metadata, schema.GetTypeDefinition("Element"), companionPath))
                    {
                        Output.Length = start;
                    }
                }
            }

            private void WritePrimitiveArray(int objectStart, string name, JsonNode values, JsonNode companions, string type, string path, string companionPath, bool rejectEmpty, bool writeCompanion)
            {
                int start = Output.Length;
                WritePropertyName(name, objectStart);
                Output.Append('[');
                int arrayStart = Output.Length;
                bool containsPayload = false;
                int count = Math.Max(SlotCount(values), SlotCount(companions));
                IType elementType = writeCompanion ? schema.GetTypeDefinition("Element") : null;
                for (int i = 0; i < count; i++)
                {
                    JsonNode value = Slot(values, i);
                    JsonNode companion = Slot(companions, i);
                    string itemPath = $"{path}[{i}]";
                    string itemCompanionPath = $"{companionPath}[{i}]";
                    JsonObject metadata = PrimitiveMetadata(value, companion, itemPath, itemCompanionPath);
                    bool hasScalar = value is not null and not JsonObject;
                    if (!writeCompanion && !hasScalar && !HasValue(metadata))
                    {
                        continue;
                    }

                    int slotStart = Output.Length;
                    if (slotStart > arrayStart)
                    {
                        Output.Append(',');
                    }

                    if (writeCompanion)
                    {
                        if (WriteComplexValue(metadata, elementType, itemCompanionPath))
                        {
                            containsPayload = true;
                        }
                        else if (hasScalar)
                        {
                            Output.Append("null");
                        }
                        else
                        {
                            Output.Length = slotStart;
                        }
                    }
                    else if (hasScalar)
                    {
                        WritePrimitiveValue(value, type, itemPath, rejectEmpty);
                        containsPayload = true;
                    }
                    else
                    {
                        Output.Append("null");
                    }
                }

                if (containsPayload)
                {
                    Output.Append(']');
                }
                else
                {
                    Output.Length = start;
                }
            }

            private static JsonObject PrimitiveMetadata(JsonNode value, JsonNode companion, string path, string companionPath)
            {
                JsonObject inline = value is JsonObject ? RequireElement(value, path) : null;
                JsonObject explicitCompanion = companion is null ? null : RequireElement(companion, companionPath);

                // Ignixa's typed adapter selects the whole inline object, not a union of fields.
                return inline ?? explicitCompanion;
            }

            private static JsonObject RequireElement(JsonNode value, string path)
            {
                if (value is not JsonObject element)
                {
                    throw Invalid(path, "Expected an Element object");
                }

                foreach ((string name, _) in element)
                {
                    if (name is not ("id" or "extension"))
                    {
                        throw Invalid(path, $"Unexpected primitive metadata property '{name}'");
                    }
                }

                return element;
            }

            private bool WriteComplexValue(JsonNode value, IType type, string path)
            {
                if (value is null)
                {
                    return false;
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

            private void WritePrimitiveValue(JsonNode value, string type, string path, bool rejectEmpty, bool normalizeInstant = false)
            {
                if (value is not JsonValue scalar)
                {
                    throw Invalid(path, $"Expected a {type} primitive");
                }

                bool isString = scalar.TryGetValue<string>(out string literal);
                if (normalizeInstant)
                {
                    if (!isString || !LastUpdatedPattern.IsMatch(literal)
                        || !DateTimeOffset.TryParse(literal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
                    {
                        throw Invalid(path, "Expected a FHIR instant or a full date");
                    }

                    WriteString(parsed.ToInstantString());
                    return;
                }

                string text = isString ? literal : scalar.ToJsonString();
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
                        if (!rejectEmpty || text.Length != 0)
                        {
                            WriteString(text);
                            return;
                        }

                        break;
                }

                throw Invalid(path, $"Invalid {type} primitive");
            }

            private static FormatException Invalid(string path, string message) => new($"{message} at '{path}'.");

            private static bool IsPrimitive(IType type) => type.Info.IsPrimitive || type.Info.Name == "xhtml";

            private (ITypeExtended Definition, IType Type) ResolveChild(IType parent, string name)
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
                                    return (metadata, schema.GetTypeDefinition(code));
                                }
                            }
                        }
                    }
                    else if (name == childName)
                    {
                        if (metadata.ContentReference is { Length: > 0 } reference)
                        {
                            return (metadata, schema.GetTypeDefinition(reference.TrimStart('#')));
                        }

                        string code = metadata.DefaultTypeName;
                        if (code is "BackboneElement" or "BackboneType" or "Element"
                            && schema.GetTypeDefinition($"{parent.Info.Name}.{childName}") is IType backbone)
                        {
                            return (metadata, backbone);
                        }

                        return (metadata, code is null ? null : schema.GetTypeDefinition(code));
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
