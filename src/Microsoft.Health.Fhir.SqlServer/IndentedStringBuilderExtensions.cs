// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer
{
    internal static class IndentedStringBuilderExtensions
    {
        /// <summary>
        /// Helps with building a WHERE clause with 0 to many predicates ANDed together.
        /// Call <see cref="IndentedStringBuilder.DelimitedScope.BeginDelimitedElement"/> before appending
        /// a predicate and be sure to dispose the the <see cref="IndentedStringBuilder.DelimitedScope"/>
        /// at the end.
        /// </summary>
        /// <param name="indentedStringBuilder">The string builder</param>
        /// <returns>The scope</returns>
        public static IndentedStringBuilder.DelimitedScope BeginDelimitedWhereClause(this IndentedStringBuilder indentedStringBuilder)
        {
            return indentedStringBuilder.BeginDelimitedScope(
                sb =>
                {
                    sb.Append("WHERE ");
                    sb.IndentLevel++;
                },
                sb =>
                {
                    sb.AppendLine();
                    sb.Append("AND ");
                },
                sb =>
                {
                    sb.IndentLevel--;
                    sb.AppendLine();
                });
        }

        public static IndentedStringBuilder.DelimitedScope BeginDelimitedOnClause(this IndentedStringBuilder indentedStringBuilder)
        {
            return indentedStringBuilder.BeginDelimitedScope(
                sb =>
                {
                    sb.Append("ON ");
                    sb.IndentLevel++;
                },
                sb =>
                {
                    sb.AppendLine();
                    sb.Append("AND ");
                },
                sb =>
                {
                    sb.IndentLevel--;
                    sb.AppendLine();
                });
        }
    }
}
