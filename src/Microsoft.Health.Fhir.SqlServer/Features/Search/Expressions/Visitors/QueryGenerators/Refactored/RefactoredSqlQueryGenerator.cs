// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.CteGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored
{
    /// <summary>
    /// Refactored SQL query generator with improved separation of concerns.
    /// Delegates to specialized components instead of handling everything in one monolithic class.
    /// </summary>
    internal class RefactoredSqlQueryGenerator : DefaultSqlExpressionVisitor<SearchOptions, object>
    {
        private const int StackOverflowLimit = 100;

        private readonly QueryGenerationContext _context;
        private readonly CteGeneratorFactory _cteGeneratorFactory;

        public RefactoredSqlQueryGenerator(
            IndentedStringBuilder sb,
            HashingSqlQueryParameterManager parameters,
            ISqlServerFhirModel model,
            SchemaInformation schemaInfo,
            SearchParamTableExpressionQueryGeneratorFactory queryGeneratorFactory,
            bool reuseQueryPlans,
            bool isAsyncOperation,
            SqlException sqlException = null)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(schemaInfo, nameof(schemaInfo));
            EnsureArg.IsNotNull(queryGeneratorFactory, nameof(queryGeneratorFactory));

            _context = new QueryGenerationContext(
                sb,
                parameters,
                model,
                schemaInfo,
                null, // SearchOptions set during Visit
                reuseQueryPlans,
                isAsyncOperation);

            _cteGeneratorFactory = CteGeneratorFactory.CreateDefault();

            if (sqlException?.Number == 8649) // QueryProcessorNoQueryPlan error number
            {
                _context.PreviousSqlQueryGeneratorFailure = true;
            }
        }

        public System.Collections.Generic.HashSet<short> SearchParamIds => _context.SearchParamIds;

        public override object VisitSqlRoot(SqlRootExpression expression, SearchOptions searchOptions)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

            _context.RootExpression = expression;
            _context.SearchOptions = searchOptions;

            if (expression.SearchParamTableExpressions.Count > 0)
            {
                GenerateQueryWithTableExpressions(expression, searchOptions);
            }
            else
            {
                GenerateSimpleQuery(expression, searchOptions);
            }

            return null;
        }

        private void GenerateQueryWithTableExpressions(SqlRootExpression expression, SearchOptions searchOptions)
        {
            if (expression.ResourceTableExpressions.Count > 0)
            {
                throw new InvalidOperationException("Expected no predicates on the Resource table because of the presence of TableExpressions");
            }

            // Declare filtered data table variable for includes
            DeclareFilteredDataTableVariable(searchOptions);

            // Generate CTEs
            _context.StringBuilder.AppendLine(";WITH");
            GenerateCommonTableExpressions(expression.SearchParamTableExpressions, searchOptions);

            // Add parameter hash
            ParameterHashBuilder.AddParametersHash(_context);

            // Generate final SELECT
            SelectClauseBuilder.BuildSelectClause(_context);
        }

        private void GenerateSimpleQuery(SqlRootExpression expression, SearchOptions searchOptions)
        {
            ParameterHashBuilder.AddParametersHash(_context);
            SelectClauseBuilder.BuildSelectClause(_context);
        }

        private void DeclareFilteredDataTableVariable(SearchOptions searchOptions)
        {
            bool hasIncludes = _context.RootExpression.SearchParamTableExpressions
                .Any(t => t.Kind == SearchParamTableExpressionKind.Include);

            if (!hasIncludes)
            {
                return;
            }

            var sb = _context.StringBuilder;
            sb.Append("DECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int");

            if (Helpers.SortingHelper.IsSortValueNeeded(searchOptions))
            {
                var sortDetails = Helpers.SortingHelper.GetSortDetails(searchOptions);
                var typeStr = sortDetails.ColumnType.ToString().ToLowerInvariant();
                sb.Append($", SortValue {typeStr}");

                if (sortDetails.RequiresLength)
                {
                    sb.Append($"({sortDetails.MaxLength})");
                }
            }

            sb.AppendLine(")");
        }

        private void GenerateCommonTableExpressions(
            System.Collections.Generic.IReadOnlyList<SearchParamTableExpression> expressions,
            SearchOptions searchOptions)
        {
            _context.StringBuilder.AppendDelimited(
                $"{Environment.NewLine},",
                expressions.SortExpressionsByQueryLogic(),
                (sb, tableExpression) =>
                {
                    GenerateSingleCte(tableExpression, searchOptions);
                });

            _context.StringBuilder.AppendLine();
        }

        private void GenerateSingleCte(SearchParamTableExpression expression, SearchOptions searchOptions)
        {
            ValidateStackDepth();

            try
            {
                var generator = _cteGeneratorFactory.GetGenerator(expression.Kind);
                generator.Generate(expression, _context);
            }
            finally
            {
                _context.StackDepth--;
            }
        }

        public override object VisitTable(SearchParamTableExpression expression, SearchOptions searchOptions)
        {
            ValidateStackDepth();

            try
            {
                var generator = _cteGeneratorFactory.GetGenerator(expression.Kind);
                generator.Generate(expression, _context);
            }
            finally
            {
                _context.StackDepth--;
            }

            return null;
        }

        private void ValidateStackDepth()
        {
            _context.StackDepth++;
            if (_context.StackDepth > StackOverflowLimit)
            {
                throw new SearchParameterTooComplexException();
            }
        }
    }
}
