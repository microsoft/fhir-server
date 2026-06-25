// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Parser for three-component composite search parameters that combine a Token parameter with two Number parameters.
    /// This is used for range-based searches where a code identifies the measurement type and two numbers define a range.
    /// Example: Observation.component-code-value-quantity combining component.code (token),
    /// component.valueQuantity.value low (number), and component.valueQuantity.value high (number).
    /// Format: "code|system$gt100$lt200" where the first part is a token and the second and third are numbers with optional prefixes.
    /// </summary>
    public class TokenNumberNumberCompositeSqlParser : BaseCompositeSqlParser
    {
        public TokenNumberNumberCompositeSqlParser(
            SearchParameterCollection parameterCollection)
            : base(parameterCollection, new TokenSqlParser(parameterCollection), new NumberSqlParser(parameterCollection), new NumberSqlParser(parameterCollection))
        {
        }
    }
}
