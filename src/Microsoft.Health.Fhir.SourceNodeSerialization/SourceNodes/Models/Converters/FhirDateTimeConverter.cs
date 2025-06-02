// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Support;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models.Converters;

public class FhirDateTimeConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException();
        }

        var node = JsonNode.Parse(ref reader);
        if (node == null || node.GetValue<string>() is not { } inputString || string.IsNullOrWhiteSpace(inputString))
        {
            // The input value is not a valid string or is null/empty.
            throw new JsonException();
        }

        return PrimitiveTypeConverter.ConvertTo<DateTimeOffset>(inputString);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.ToFhirDateTime(), options);
    }
}
