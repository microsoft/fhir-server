// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators
{
    /// <summary>
    /// Generates Common Table Expressions (CTEs) for specific search parameter table expression kinds.
    /// Replaces large switch statements with strategy pattern.
    /// </summary>
    internal interface ICteGenerator
    {
        /// <summary>
        /// Determines if this generator can handle the given expression kind.
        /// </summary>
        bool CanGenerate(SearchParamTableExpressionKind kind);

        /// <summary>
        /// Generates the CTE SQL for the given expression.
        /// </summary>
        void Generate(SearchParamTableExpression expression, QueryGenerationContext context);
    }
}
