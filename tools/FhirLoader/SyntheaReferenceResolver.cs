// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Internal.FhirLoader
{
    /// <summary>
    /// Utility class for resolving Synthea bundle references
    /// </summary>
    public static class SyntheaReferenceResolver
    {
        /// <summary>
        /// Resolves all UUIDs in Synthea bundle
        /// </summary>
        /// <param name="bundle">The Bundle</param>
        public static void GivenConvertUuiDs(JObject bundle)
        {
            GivenConvertUuiDs(bundle, GivenCreateUuidLookUpTable(bundle));
        }

        private static void GivenConvertUuiDs(JToken tok, Dictionary<string, IdTypePair> idLookupTable)
        {
            switch (tok.Type)
            {
                case JTokenType.Object:
                case JTokenType.Array:

                    foreach (var c in tok.Children())
                    {
                        GivenConvertUuiDs(c, idLookupTable);
                    }

                    return;
                case JTokenType.Property:
                    var prop = (JProperty)tok;

                    if (prop.Value.Type == JTokenType.String &&
                        prop.Name == "reference" &&
                        idLookupTable.TryGetValue(prop.Value.ToString(), out var idTypePair))
                    {
                        prop.Value = idTypePair.ResourceType + "/" + idTypePair.Id;
                    }
                    else
                    {
                        GivenConvertUuiDs(prop.Value, idLookupTable);
                    }

                    return;
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Float:
                case JTokenType.Integer:
                case JTokenType.Date:
                    return;
                default:
                    throw new NotSupportedException($"Invalid token type {tok.Type} encountered");
            }
        }

        private static Dictionary<string, IdTypePair> GivenCreateUuidLookUpTable(JObject bundle)
        {
            var table = new Dictionary<string, IdTypePair>();
            var entry = (JArray)bundle["entry"];

            if (entry == null)
            {
                throw new ArgumentException("Unable to find bundle entries for creating lookup table");
            }

            try
            {
                foreach (var resourceWrapper in entry)
                {
                    var resource = resourceWrapper["resource"];
                    var fullUrl = (string)resourceWrapper["fullUrl"];
                    var resourceType = (string)resource["resourceType"];
                    var id = (string)resource["id"];

                    table.Add(fullUrl, new IdTypePair { ResourceType = resourceType, Id = id });
                }
            }
            catch
            {
                Console.WriteLine("Error parsing resources in bundle");
                throw;
            }

            return table;
        }

        private class IdTypePair
        {
            public string Id { get; set; }

            public string ResourceType { get; set; }
        }
    }
}
