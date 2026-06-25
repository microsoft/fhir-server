// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Parser for composite search parameters that combine a Token parameter with a Quantity parameter.
    /// Example: Observation.code-value-quantity combining code (token) and valueQuantity (quantity).
    /// Format: "code|system$gt100||mg" where the first part is a token and the second is a quantity with optional prefix.
    /// Quantity format: [prefix]value[|system|code] (e.g., "5.4|http://unitsofmeasure.org|mg" or "gt5.4").
    /// </summary>
    public class TokenQuantityCompositeSqlParser : BaseCompositeSqlParser
    {
        public TokenQuantityCompositeSqlParser(
            SearchParameterCollection parameterCollection)
            : base(parameterCollection, new TokenSqlParser(parameterCollection), new NumberSqlParser(parameterCollection))
        {
        }
    }
}
