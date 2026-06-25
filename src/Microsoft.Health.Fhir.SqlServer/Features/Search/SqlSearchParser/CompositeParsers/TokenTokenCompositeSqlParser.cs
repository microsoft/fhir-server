// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Parser for composite search parameters that combine two Token parameters.
    /// Example: Observation.code-value-concept combining code (token) and valueCodeableConcept (token).
    /// Format: "code1|system1$code2|system2" where both parts are token parameters.
    /// </summary>
    public class TokenTokenCompositeSqlParser : BaseCompositeSqlParser
    {
        public TokenTokenCompositeSqlParser(
            SearchParameterCollection parameterCollection)
            : base(parameterCollection, new TokenSqlParser(parameterCollection), new TokenSqlParser(parameterCollection))
        {
        }
    }
}
