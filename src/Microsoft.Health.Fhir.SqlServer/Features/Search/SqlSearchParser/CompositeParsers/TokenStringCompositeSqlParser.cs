// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Parser for composite search parameters that combine a Token parameter with a String parameter.
    /// Example: Patient.identifier combining identifier.system (token) and identifier.value (string).
    /// Format: "system|code$stringvalue" where the first part is a token and the second is a string.
    /// </summary>
    public class TokenStringCompositeSqlParser : BaseCompositeSqlParser
    {
        public TokenStringCompositeSqlParser(
            SearchParameterCollection parameterCollection)
            : base(parameterCollection, new TokenSqlParser(parameterCollection), new StringSqlParser(parameterCollection))
        {
        }
    }
}
