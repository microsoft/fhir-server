// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents search field name.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Respresents a search parameter types for FHIR")]
    public enum FieldName
    {
        CompositeCode,
        CompositeSystem,
        DateTimeStart,
        DateTimeEnd,
        Number,
        ParamName,
        QuantityCode,
        QuantitySystem,
        Quantity,
        Reference,
        String,
        TokenCode,
        TokenSystem,
        TokenText,
        Uri,
    }
}
