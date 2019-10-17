// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Utility;

namespace Microsoft.Health.Fhir.ValueSets
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720: Identifiers should not contain type names", Justification = "Specified in the FHIR spec")]
    public enum SearchParamType
    {
        [EnumLiteral("number")]
        Number,
        [EnumLiteral("date")]
        Date,
        [EnumLiteral("string")]
        String,
        [EnumLiteral("token")]
        Token,
        [EnumLiteral("reference")]
        Reference,
        [EnumLiteral("quantity")]
        Quantity,
        [EnumLiteral("uri")]
        Uri,
        [EnumLiteral("composite")]
        Composite,
        [EnumLiteral("special")]
        Special,
    }
}
