// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Parser for composite search parameters that combine a Token parameter with a Date parameter.
    /// Example: Condition.code-onset-date combining code (token) and onsetDateTime (date).
    /// Format: "code|system$ge2020-01-01" where the first part is a token and the second is a date with optional prefix.
    /// Date format: [prefix]YYYY-MM-DD or YYYY-MM-DDTHH:MM:SS (e.g., "2020-01-01" or "ge2020-01-01").
    /// </summary>
    public class TokenDateCompositeSqlParser : BaseCompositeSqlParser
    {
        public TokenDateCompositeSqlParser(
            SearchParameterCollection parameterCollection)
            : base(parameterCollection, new TokenSqlParser(parameterCollection), new DateTimeSqlParser(parameterCollection))
        {
        }
    }
}
