// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
    /// <summary>
    /// Fixed raw JSON inputs used to characterize the import parser seam across FHIR versions.
    /// </summary>
    internal static class ImportResourceParserCharacterizationCorpus
    {
        internal const string NestedPropertiesFirstOrder = """
            {
              "resourceType":"Patient",
              "id":"characterization-nested",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "name":[{"family":"Lovelace","given":["Ada","Augusta"],"use":"official"}]
            }
            """;

        internal const string NestedPropertiesSecondOrder = """
            {
              "name":[{"use":"official","given":["Ada","Augusta"],"family":"Lovelace"}],
              "meta":{"lastUpdated":"2024-02-29T10:11:12.120Z","versionId":"9"},
              "id":"characterization-nested",
              "resourceType":"Patient"
            }
            """;

        internal const string ArrayOrderAndEscapedWhitespace = """
            {
              "resourceType":"Patient",
              "id":"characterization-whitespace",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "name":[{
                "family":"Lovelace",
                "given":["Ada","Augusta"],
                "text":"  Ada\tLovelace\n\"quoted\"  "
              }]
            }
            """;

        internal const string FirstGivenArrayOrder = """
            {
              "resourceType":"Patient",
              "id":"characterization-array-order",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "name":[{"family":"Lovelace","given":["Ada","Augusta"]}]
            }
            """;

        internal const string SecondGivenArrayOrder = """
            {
              "resourceType":"Patient",
              "id":"characterization-array-order",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "name":[{"family":"Lovelace","given":["Augusta","Ada"]}]
            }
            """;

        internal const string DecimalAndTemporal = """
            {
              "resourceType":"Observation",
              "id":"characterization-decimal",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "status":"final",
              "code":{"text":"body temperature"},
              "effectiveDateTime":"2024-02-29T10:11:12.120Z",
              "valueQuantity":{"value":1.2300,"unit":"C"}
            }
            """;

        internal const string DateTypedValueWithZuluDateTime = """
            {
              "resourceType":"Patient",
              "id":"characterization-date",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "birthDate":"2024-02-29T10:11:12.120Z"
            }
            """;

        internal const string ExtensionOnlyPrimitiveAndAlignedArray = """
            {
              "resourceType":"Patient",
              "id":"characterization-primitive",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "active":null,
              "_active":{
                "extension":[{
                  "url":"http://hl7.org/fhir/StructureDefinition/data-absent-reason",
                  "valueCode":"unknown"
                }]
              },
              "name":[{
                "family":"Lovelace",
                "given":[null,"Ada"],
                "_given":[{
                  "extension":[{
                    "url":"http://hl7.org/fhir/StructureDefinition/data-absent-reason",
                    "valueCode":"masked"
                  }]
                },null]
              }]
            }
            """;

        internal const string DuplicateKnownProperty = """
            {
              "resourceType":"Patient",
              "id":"characterization-duplicate",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "active":false,
              "active":true
            }
            """;

        internal const string UnknownProperty = """
            {
              "resourceType":"Patient",
              "id":"characterization-unknown",
              "meta":{"versionId":"9","lastUpdated":"2024-02-29T10:11:12.120Z"},
              "unrecognizedProperty":{"mustNot":"persist"}
            }
            """;

        internal const string MalformedScalarShape = """
            {
              "resourceType":"Patient",
              "id":"characterization-malformed",
              "active":[true]
            }
            """;
    }
}
