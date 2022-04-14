// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class InQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        internal static readonly InQueryGenerator Instance = new InQueryGenerator();

        public override Table Table => null;

        // FHIBF: Now I'm doubt what would be the ideal behavior of this class.
        // Should we create a Query Geneator for the IN operator?
        // If yes, then how could I create the ideal instance "Column" to be used in it, giving the expression contains only the FieldName?

        // public override SearchParameterQueryGeneratorContext VisitIn(InExpression expression, SearchParameterQueryGeneratorContext context)
        // {
        //    return VisitSimpleIn(context, expression.FieldName, expression.Values);
        // }
    }
}
