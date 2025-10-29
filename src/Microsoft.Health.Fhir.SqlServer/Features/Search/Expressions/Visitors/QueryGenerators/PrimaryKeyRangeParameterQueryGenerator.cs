// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Generates predicates for <see cref="SqlSearchParameters.PrimaryKeyParameter"/>.
    /// These take the from of
    /// ResourceTypeId = currentTypeId AND ResourceSurrogateId > currentSurrogateId OR ResourceTypeId IN (subsequentIDs)
    /// </summary>
    internal class PrimaryKeyRangeParameterQueryGenerator : ResourceTableSearchParameterQueryGenerator
    {
        public static new readonly PrimaryKeyRangeParameterQueryGenerator Instance = new();

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            var primaryKeyRange = (PrimaryKeyRange)expression.Value;

            context.StringBuilder.AppendLine("(");
            using (context.StringBuilder.Indent())
            {
                VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.Resource.ResourceTypeId, null, primaryKeyRange.CurrentValue.ResourceTypeId, includeInParameterHash: false);
                context.StringBuilder.Append(" AND ");
                VisitSimpleBinary(expression.BinaryOperator, context, VLatest.Resource.ResourceSurrogateId, null, primaryKeyRange.CurrentValue.ResourceSurrogateId, includeInParameterHash: false);

                bool first = true;
                for (short i = 0; i < primaryKeyRange.NextResourceTypeIds.Count; i++)
                {
                    if (primaryKeyRange.NextResourceTypeIds[i])
                    {
                        if (first)
                        {
                            context.StringBuilder.AppendLine();
                            context.StringBuilder.Append("OR ");
                            AppendColumnName(context, VLatest.Resource.ResourceTypeId, (int?)null).Append(" IN (");
                            first = false;
                        }
                        else
                        {
                            context.StringBuilder.Append(", ");
                        }

                        context.StringBuilder.Append(context.Parameters.AddParameter(VLatest.Resource.ResourceTypeId, i, includeInHash: false));
                    }
                }

                if (!first)
                {
                    context.StringBuilder.AppendLine(")");
                }
            }

            context.StringBuilder.AppendLine(")");

            return context;
        }

        public override SearchParameterQueryGeneratorContext VisitIn<T>(InExpression<T> expression, SearchParameterQueryGeneratorContext context)
        {
            if (typeof(T) != typeof(PrimaryKeyValue))
            {
                throw new InvalidOperationException($"Unexpected value type {typeof(T)} for primary key IN expression.");
            }

            var primaryKeyValues = expression.Values.Cast<PrimaryKeyValue>().ToArray();

            context.StringBuilder.AppendLine("(");
            using (context.StringBuilder.Indent())
            {
                for (int i = 0; i < primaryKeyValues.Length; i++)
                {
                    if (i > 0)
                    {
                        context.StringBuilder.AppendLine();
                        context.StringBuilder.Append("OR ");
                    }

                    var primaryKey = primaryKeyValues[i];

                    context.StringBuilder.Append("(");
                    VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.Resource.ResourceTypeId, null, primaryKey.ResourceTypeId, includeInParameterHash: false);
                    context.StringBuilder.Append(" AND ");
                    VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.Resource.ResourceSurrogateId, null, primaryKey.ResourceSurrogateId, includeInParameterHash: false);
                    context.StringBuilder.Append(")");
                }
            }

            context.StringBuilder.AppendLine(")");

            return context;
        }
    }
}
