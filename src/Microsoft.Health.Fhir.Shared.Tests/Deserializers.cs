// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
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
        private static readonly Regex MessageChecker = new Regex("Type checking the data: Literal '(.*)' cannot be parsed as a (.*). \\(at (.*)\\)", RegexOptions.Compiled);

        public static ResourceDeserializer ResourceDeserializer => new ResourceDeserializer(
            (FhirResourceFormat.Json, (str, version, lastModified) =>
                {
                    Resource resource = null;

                    Parse:
                    try
                    {
                        resource = JsonParser.Parse<Resource>(str);
                    }
                    catch (StructuralTypeException ex)
                    {
                        var match = MessageChecker.Match(ex.Message);
                        if (match.Success && match.Groups.Count == 4 && match.Groups[2].Value == "date")
                        {
                            var valueToReplace = match.Groups[1].Value;
                            var location = match.Groups[3].Value;
                            var replace = valueToReplace.Substring(0, 10);
                            var root = FhirJsonNode.Parse(str, "Resource");
                            var currentNode = root;
                            while (currentNode != null)
                            {
                                foreach (var child in currentNode.Children())
                                {
                                    if (location.StartsWith(child.Location))
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
                            goto Parse;
                        }
                    }

                    resource.VersionId = version;
                    resource.Meta.LastUpdated = lastModified;
                    return resource.ToTypedElement().ToResourceElement();
                }));
    }
}
