// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Utility;

namespace Microsoft.Health.Fhir.ValueSets
{
    public enum SearchComparator
    {
        [EnumLiteral("eq", "http://hl7.org/fhir/search-comparator")]
        Eq,

        [EnumLiteral("ne", "http://hl7.org/fhir/search-comparator")]
        Ne,

        [EnumLiteral("gt", "http://hl7.org/fhir/search-comparator")]
        Gt,

        [EnumLiteral("lt", "http://hl7.org/fhir/search-comparator")]
        Lt,

        [EnumLiteral("ge", "http://hl7.org/fhir/search-comparator")]
        Ge,

        [EnumLiteral("le", "http://hl7.org/fhir/search-comparator")]
        Le,

        [EnumLiteral("sa", "http://hl7.org/fhir/search-comparator")]
        Sa,

        [EnumLiteral("eb", "http://hl7.org/fhir/search-comparator")]
        Eb,

        [EnumLiteral("ap", "http://hl7.org/fhir/search-comparator")]
        Ap,
    }
}
