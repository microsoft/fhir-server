// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
                context.StringBuilder.AppendLine();
                context.StringBuilder.Append("OR ");
                AppendColumnName(context, VLatest.Resource.ResourceTypeId, (int?)null).Append(" IN (");

                bool first = true;
                for (short i = 0; i < primaryKeyRange.NextResourceTypeIds.Count; i++)
                {
                    if (primaryKeyRange.NextResourceTypeIds[i])
                    {
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            context.StringBuilder.Append(", ");
                        }

                        context.StringBuilder.Append(context.Parameters.AddParameter(VLatest.Resource.ResourceTypeId, i, includeInHash: false));
                    }
                }

                context.StringBuilder.AppendLine(")");
            }

            context.StringBuilder.AppendLine(")");

            return context;
        }
    }
}
