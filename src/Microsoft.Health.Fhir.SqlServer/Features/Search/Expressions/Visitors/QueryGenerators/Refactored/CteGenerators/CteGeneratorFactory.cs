// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.ClauseBuilders;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators
{
    /// <summary>
    /// Factory for creating and selecting appropriate CTE generators.
    /// Uses chain of responsibility pattern to handle different expression kinds.
    /// </summary>
    internal class CteGeneratorFactory
    {
        private readonly IEnumerable<ICteGenerator> _generators;

        public CteGeneratorFactory(IEnumerable<ICteGenerator> generators)
        {
            _generators = generators;
        }

        public ICteGenerator GetGenerator(SearchParamTableExpressionKind kind)
        {
            return _generators.FirstOrDefault(g => g.CanGenerate(kind))
                   ?? throw new System.ArgumentException($"No generator found for kind: {kind}", nameof(kind));
        }

        public static CteGeneratorFactory CreateDefault()
        {
            var historyBuilder = new HistoryClauseBuilder();
            var deletedBuilder = new DeletedClauseBuilder();

            var generators = new List<ICteGenerator>
            {
                new NormalCteGenerator(historyBuilder),
                new UnionCteGenerator(historyBuilder, deletedBuilder),
                new ChainCteGenerator(historyBuilder),
                new IncludeCteGenerator(historyBuilder),
                new SortCteGenerator(historyBuilder),
                new NotExistsCteGenerator(historyBuilder),
                new TopCteGenerator(),
            };

            return new CteGeneratorFactory(generators);
        }
    }
}
