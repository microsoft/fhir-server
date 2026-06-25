// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Parser for composite search parameters that combine a Token parameter with a Number parameter.
    /// Example: Observation.component-code-value-quantity combining component.code (token) and component.value (number).
    /// Format: "code|system$gt100" where the first part is a token and the second is a number with optional prefix.
    /// </summary>
    public class TokenNumberCompositeSqlParser : BaseCompositeSqlParser
    {
        public TokenNumberCompositeSqlParser(
            SearchParameterCollection parameterCollection)
            : base(parameterCollection, new TokenSqlParser(parameterCollection), new NumberSqlParser(parameterCollection))
        {
        }
    }
}
