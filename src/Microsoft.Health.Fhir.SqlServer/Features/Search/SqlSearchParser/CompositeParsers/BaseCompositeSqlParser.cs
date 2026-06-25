// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers
{
    /// <summary>
    /// Base parser for composite search parameters that combine multiple component search parameters.
    /// Composite parameters use the '$' separator to join component values (e.g., "code$value" or "code$low$high").
    /// Supports both two-component and three-component composite parameters.
    /// </summary>
    public abstract class BaseCompositeSqlParser : BaseSqlParser
    {
        private readonly BaseSqlParser _firstComponentParser;
        private readonly BaseSqlParser _secondComponentParser;
        private readonly BaseSqlParser? _thirdComponentParser;

        protected BaseCompositeSqlParser(
            SearchParameterCollection parameterCollection,
            BaseSqlParser firstComponentParser,
            BaseSqlParser secondComponentParser,
            BaseSqlParser? thirdComponentParser = null)
            : base(parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(firstComponentParser);
            ArgumentNullException.ThrowIfNull(secondComponentParser);

            _firstComponentParser = firstComponentParser;
            _secondComponentParser = secondComponentParser;
            _thirdComponentParser = thirdComponentParser;
        }

        /// <summary>
        /// Gets the first component parser.
        /// </summary>
        protected ISqlParser FirstComponentParser => _firstComponentParser;

        /// <summary>
        /// Gets the second component parser.
        /// </summary>
        protected ISqlParser SecondComponentParser => _secondComponentParser;

        /// <summary>
        /// Gets the third component parser (null for two-component composites).
        /// </summary>
        protected ISqlParser? ThirdComponentParser => _thirdComponentParser;

        /// <summary>
        /// Gets whether this is a three-component composite.
        /// </summary>
        protected bool IsThreeComponent => _thirdComponentParser != null;

        /// <summary>
        /// Determines the composite type from a SearchParameterInfo by examining its component definitions.
        /// </summary>
        /// <param name="searchParameter">The search parameter to analyze.</param>
        /// <param name="parameterLookup">Function to resolve component search parameters by URL.</param>
        /// <returns>The determined composite type.</returns>
        public static CompositeType DetermineCompositeType(
            SearchParameterInfo searchParameter,
            SearchParameterCollection searchParameterCollection,
            int resourceTypeId)
        {
            ArgumentNullException.ThrowIfNull(searchParameter);
            ArgumentNullException.ThrowIfNull(searchParameterCollection);

            if (searchParameter.Type != SearchParamType.Composite)
            {
                return CompositeType.Unknown;
            }

            if (searchParameter.Component == null || (searchParameter.Component.Count != 2 && searchParameter.Component.Count != 3))
            {
                // Composite parameters must have exactly 2 or 3 components
                return CompositeType.Unknown;
            }

            var firstComponent = searchParameterCollection.GetByCode(searchParameter.Component[0].DefinitionUrl, resourceTypeId);
            var secondComponent = searchParameterCollection.GetByCode(searchParameter.Component[1].DefinitionUrl, resourceTypeId);

            if (firstComponent == null || secondComponent == null)
            {
                return CompositeType.Unknown;
            }

            // Check for three-component composite
            if (searchParameter.Component.Count == 3)
            {
                var thirdComponent = searchParameterCollection.GetByCode(searchParameter.Component[2].DefinitionUrl, resourceTypeId);
                if (thirdComponent == null)
                {
                    return CompositeType.Unknown;
                }

                // Currently only Token-Number-Number is supported
                return (firstComponent.Type, secondComponent.Type, thirdComponent.Type) switch
                {
                    (SearchParamType.Token, SearchParamType.Number, SearchParamType.Number) => CompositeType.TokenNumberNumber,
                    _ => CompositeType.Unknown,
                };
            }

            // Determine composite type based on component types (two-component)
            return (firstComponent.Type, secondComponent.Type) switch
            {
                (SearchParamType.Token, SearchParamType.Token) => CompositeType.TokenToken,
                (SearchParamType.Token, SearchParamType.Quantity) => CompositeType.TokenQuantity,
                (SearchParamType.Token, SearchParamType.String) => CompositeType.TokenString,
                (SearchParamType.Token, SearchParamType.Number) => CompositeType.TokenNumber,
                (SearchParamType.Token, SearchParamType.Date) => CompositeType.TokenDate,
                (SearchParamType.Token, SearchParamType.Reference) => CompositeType.TokenReference,
                _ => CompositeType.Unknown,
            };
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null)
        {
            // Composite parameters use '$' as separator between component values
            // Two-component example: "http://loinc.org|1234-5$gt100" for a token$number composite
            // Three-component example: "http://loinc.org|1234-5$gt100$lt200" for a token$number$number composite
            var components = value.Split('$');

            if (_thirdComponentParser != null)
            {
                // Three-component composite
                if (components.Length != 3)
                {
                    throw new InvalidOperationException(
                        $"Three-component composite search parameter value must contain exactly two '$' separators. Got: {value}");
                }

                var firstValue = components[0];
                var secondValue = components[1];
                var thirdValue = components[2];

                // Build WHERE clause that combines all three component conditions
                return BuildThreeComponentWhereClause(firstValue, secondValue, thirdValue, modifier);
            }
            else
            {
                // Two-component composite
                if (components.Length != 2)
                {
                    throw new InvalidOperationException(
                        $"Two-component composite search parameter value must contain exactly one '$' separator. Got: {value}");
                }

                var firstValue = components[0];
                var secondValue = components[1];

                // Build WHERE clause that combines both component conditions
                return BuildCompositeWhereClause(firstValue, secondValue, modifier);
            }
        }

        /// <summary>
        /// Builds the WHERE clause for a two-component composite search parameter by combining both component conditions.
        /// </summary>
        /// <param name="firstValue">The value for the first component.</param>
        /// <param name="secondValue">The value for the second component.</param>
        /// <param name="modifier">The modifier applied to the search parameter (if any).</param>
        /// <returns>The SQL WHERE clause combining both components.</returns>
        protected string BuildCompositeWhereClause(string firstValue, string secondValue, string modifier)
        {
            if (_thirdComponentParser != null)
            {
                throw new NotSupportedException("Two-component composite search parameters are not supported by this parser.");
            }

            // Pass column suffix 1 for first component, 2 for second component
            var firstWhereClause = _firstComponentParser.BuildWhereClause(firstValue, modifier, columnSuffix: 1);
            var secondWhereClause = _secondComponentParser.BuildWhereClause(secondValue, modifier, columnSuffix: 2);

            return $"({firstWhereClause}) AND ({secondWhereClause})";
        }

        /// <summary>
        /// Builds the WHERE clause for a three-component composite search parameter by combining all three component conditions.
        /// Override this method in derived classes that support three-component composites.
        /// </summary>
        /// <param name="firstValue">The value for the first component.</param>
        /// <param name="secondValue">The value for the second component.</param>
        /// <param name="thirdValue">The value for the third component.</param>
        /// <param name="modifier">The modifier applied to the search parameter (if any).</param>
        /// <returns>The SQL WHERE clause combining all three components.</returns>
        protected string BuildThreeComponentWhereClause(string firstValue, string secondValue, string thirdValue, string modifier)
        {
            if (_thirdComponentParser == null)
            {
                throw new NotSupportedException("Three-component composite search parameters are not supported by this parser.");
            }

            // Pass column suffix 1, 2, 3 for each component respectively
            var firstWhereClause = _firstComponentParser.BuildWhereClause(firstValue, modifier, columnSuffix: 1);
            var secondWhereClause = _secondComponentParser.BuildWhereClause(secondValue, modifier, columnSuffix: 2);
            var thirdWhereClause = _thirdComponentParser.BuildWhereClause(thirdValue, modifier, columnSuffix: 3);

            return $"({firstWhereClause}) AND ({secondWhereClause}) AND ({thirdWhereClause})";
        }
    }
}
