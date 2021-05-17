// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class Deserializers
    {
        private static readonly FhirJsonParser JsonParser = new FhirJsonParser(DefaultParserSettings.Settings);
        private static readonly Regex MessageChecker = new Regex("Type checking the data: Literal '(?<value>.*)' cannot be parsed as a (?<type>.*). \\(at (?<location>.*)\\)", RegexOptions.Compiled);

        public static ResourceDeserializer ResourceDeserializer => new ResourceDeserializer((FhirResourceFormat.Json, ConvertJson));

        private static ResourceElement ConvertJson(string str, string version, DateTimeOffset lastModified)
        {
            Resource resource = null;
            var parsed = false;
            var i = 0;
            do
            {
                try
                {
                    resource = JsonParser.Parse<Resource>(str);
                    parsed = true;
                }
                catch (StructuralTypeException ex)
                {
                    var match = MessageChecker.Match(ex.Message);
                    if (match.Success && match.Groups.Count == 4 && match.Groups["type"].Value == "date")
                    {
                        i++;
                        if (i > 100)
                        {
                            throw;
                        }

                        var valueToReplace = match.Groups["value"].Value;
                        var location = match.Groups["location"].Value;
                        var replace = valueToReplace.Substring(0, 10);
                        var root = FhirJsonNode.Parse(str, KnownResourceTypes.Resource);
                        var currentNode = root;
                        while (currentNode != null)
                        {
                            foreach (var child in currentNode.Children())
                            {
                                if (location.StartsWith(child.Location, StringComparison.OrdinalIgnoreCase))
                                {
                                    currentNode = child;
                                    break;
                                }
                            }

                            if (currentNode.Location == location)
                            {
                                break;
                            }
                        }

                        (currentNode as FhirJsonNode).JsonValue.Value = replace;
                        str = root.ToJson();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            while (!parsed);

            resource.VersionId = version;
            resource.Meta.LastUpdated = lastModified;
            return resource.ToTypedElement().ToResourceElement();
        }
    }
}
