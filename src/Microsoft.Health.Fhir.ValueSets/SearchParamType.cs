// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.ValueSets
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720: Identifiers should not contain type names", Justification = "Specified in the FHIR spec")]
    public enum SearchParamType
    {
        Number,
        Date,
        String,
        Token,
        Reference,
        Quantity,
        Uri,
        Composite,
        Special,
    }
}
