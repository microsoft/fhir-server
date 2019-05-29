// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer
{
    internal static class IndentedStringBuilderExtensions
    {
        public static IndentedStringBuilder.DelimitedScope DelimitWhereClause(this IndentedStringBuilder indentedStringBuilder)
        {
            return indentedStringBuilder.Delimit(
                sb =>
                {
                    sb.IndentLevel++;
                    sb.Append("WHERE ");
                },
                sb =>
                {
                    sb.AppendLine();
                    sb.Append("AND ");
                },
                sb =>
                {
                    sb.AppendLine();
                    sb.IndentLevel--;
                });
        }
    }
}
