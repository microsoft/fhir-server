// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceTypeIdResourceSurrogateKeySetParameterQueryGenerator : ResourceTableSearchParameterQueryGenerator
    {
        public static new readonly ResourceTypeIdResourceSurrogateKeySetParameterQueryGenerator Instance = new();

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            var keySetValue = (KeySetValue)expression.Value;

            context.StringBuilder.AppendLine("(");
            using (context.StringBuilder.Indent())
            {
                VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.Resource.ResourceTypeId, null, keySetValue.CurrentPositionResourceTypeId);
                context.StringBuilder.Append(" AND ");
                VisitSimpleBinary(expression.BinaryOperator, context, VLatest.Resource.ResourceSurrogateId, null, keySetValue.CurrentPositionResourceSurrogateId);
                context.StringBuilder.AppendLine();
                context.StringBuilder.Append("OR ");
                AppendColumnName(context, VLatest.Resource.ResourceTypeId, (int?)null)
                    .Append(" IN (");

                bool first = true;
                for (short i = 0; i < keySetValue.NextResourceTypeIds.Count; i++)
                {
                    if (keySetValue.NextResourceTypeIds[i])
                    {
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            context.StringBuilder.Append(", ");
                        }

                        context.StringBuilder.Append(context.Parameters.AddParameter(VLatest.Resource.ResourceTypeId, i));
                    }
                }

                context.StringBuilder.AppendLine(")");
            }

            context.StringBuilder.AppendLine(")");

            return context;
        }
    }
}
