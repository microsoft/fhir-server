// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class StringSqlParser : BaseSqlParser
    {
        public StringSqlParser(SearchParameterCollection parameterCollection)
            : base(parameterCollection)
        {
        }

        protected override string BuildWhereClause(string value)
        {
            var escapedValue = EscapeSqlValue(value);
            return $"t.Text = {escapedValue}";
        }
    }
}
