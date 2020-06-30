// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Utility;

namespace Microsoft.Health.Fhir.ValueSets
{
    public enum SearchModifierCode
    {
        [EnumLiteral("missing", "http://hl7.org/fhir/search-modifier-code")]
        Missing,

        [EnumLiteral("exact", "http://hl7.org/fhir/search-modifier-code")]
        Exact,

        [EnumLiteral("contains", "http://hl7.org/fhir/search-modifier-code")]
        Contains,

        [EnumLiteral("not", "http://hl7.org/fhir/search-modifier-code")]
        Not,

        [EnumLiteral("text", "http://hl7.org/fhir/search-modifier-code")]
        Text,

        [EnumLiteral("in", "http://hl7.org/fhir/search-modifier-code")]
        In,

        [EnumLiteral("not-in", "http://hl7.org/fhir/search-modifier-code")]
        NotIn,

        [EnumLiteral("below", "http://hl7.org/fhir/search-modifier-code")]
        Below,

        [EnumLiteral("above", "http://hl7.org/fhir/search-modifier-code")]
        Above,

        [EnumLiteral("type", "http://hl7.org/fhir/search-modifier-code")]
        Type,

        [EnumLiteral("identifier", "http://hl7.org/fhir/search-modifier-code")]
        Identifier,

        [EnumLiteral("ofType", "http://hl7.org/fhir/search-modifier-code")]
        OfType,
    }
}
