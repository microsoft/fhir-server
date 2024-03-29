﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression.
    /// </summary>
    public abstract class Expression
    {
        /// <summary>
        /// Creates a <see cref="SearchParameterExpression"/> that represents a set of ANDed expressions over a search parameter.
        /// </summary>
        /// <param name="searchParameter">The search parameter this expression is bound to.</param>
        /// <param name="expression">The expression over the parameter's values.</param>
        /// <returns>A <see cref="SearchParameterExpression"/>.</returns>
        public static SearchParameterExpression SearchParameter(SearchParameterInfo searchParameter, Expression expression)
        {
            return new SearchParameterExpression(searchParameter, expression);
        }

        /// <summary>
        /// Creates a <see cref="MissingSearchParameterExpression"/> that represents a search parameter being present or not in a resource.
        /// </summary>
        /// <param name="searchParameter">The search parameter this expression is bound to.</param>
        /// <param name="isMissing">A flag indicating whether the parameter should be missing or not.</param>
        /// <returns>A <see cref="SearchParameterExpression"/>.</returns>
        public static MissingSearchParameterExpression MissingSearchParameter(SearchParameterInfo searchParameter, bool isMissing)
        {
            return new MissingSearchParameterExpression(searchParameter, isMissing);
        }

        /// <summary>
        /// Creates a <see cref="NotExpression"/> that represents logical NOT operation over <paramref name="expression"/>.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <returns>A <see cref="NotExpression"/> that has logical operator NOT over <paramref name="expression"/>.</returns>
        public static NotExpression Not(Expression expression)
        {
            return new NotExpression(expression);
        }

        /// <summary>
        /// Creates a <see cref="MultiaryExpression"/> that represents logical AND operation over <paramref name="expressions"/>.
        /// </summary>
        /// <param name="expressions">The expressions.</param>
        /// <returns>A <see cref="MultiaryExpression"/> that has <see cref="MultiaryOperator"/> of AND on all <paramref name="expressions"/>.</returns>
        public static MultiaryExpression And(params Expression[] expressions)
        {
            return new MultiaryExpression(MultiaryOperator.And, expressions);
        }

        /// <summary>
        /// Creates a <see cref="MultiaryExpression"/> that represents logical AND operation over <paramref name="expressions"/>.
        /// </summary>
        /// <param name="expressions">The expressions.</param>
        /// <returns>A <see cref="MultiaryExpression"/> that has <see cref="MultiaryOperator"/> of AND on all <paramref name="expressions"/>.</returns>
        public static MultiaryExpression And(IReadOnlyList<Expression> expressions)
        {
            return new MultiaryExpression(MultiaryOperator.And, expressions);
        }

        /// <summary>
        /// Creates a <see cref="ChainedExpression"/> that represents chained operation.
        /// </summary>
        /// <param name="resourceTypes">The resource type.</param>
        /// <param name="referenceSearchParameter">The search parameter that establishes the reference between resources</param>
        /// <param name="targetResourceTypes">The target resource type.</param>
        /// <param name="reversed">If this is a reversed chained expression.</param>
        /// <param name="expression">The expression.</param>
        /// <returns>A <see cref="ChainedExpression"/> that represents chained operation on <paramref name="targetResourceTypes"/> through <paramref name="referenceSearchParameter"/>.</returns>
        public static ChainedExpression Chained(string[] resourceTypes, SearchParameterInfo referenceSearchParameter, string[] targetResourceTypes, bool reversed, Expression expression)
        {
            return new ChainedExpression(resourceTypes, referenceSearchParameter, targetResourceTypes, reversed, expression);
        }

        /// <summary>
        /// Creates a <see cref="IncludeExpression"/> that represents an include operation.
        /// </summary>
        /// <param name="resourceTypes">The resource that supports the reference.</param>
        /// <param name="referenceSearchParameter">The search parameter that establishes the reference between resources</param>
        /// <param name="sourceResourceType">The source resource type (used in revinclude).</param>
        /// <param name="targetResourceType">The target resource type (used in include).</param>
        /// <param name="referencedTypes">The type of resources referenced by resourceType</param>
        /// <param name="wildCard">If this is a wildcard include.</param>
        /// <param name="reversed">If this is a reversed include (revinclude) expression.</param>
        /// <param name="iterate">If this is include has :iterate (:recurse) modifier.</param>
        /// <returns>A <see cref="IncludeExpression"/> that represents an include on <paramref name="targetResourceType" /> through <paramref name="referenceSearchParameter" />.</returns>
        public static IncludeExpression Include(string[] resourceTypes, SearchParameterInfo referenceSearchParameter, string sourceResourceType, string targetResourceType, IEnumerable<string> referencedTypes, bool wildCard, bool reversed, bool iterate)
        {
            return new IncludeExpression(resourceTypes, referenceSearchParameter, sourceResourceType, targetResourceType, referencedTypes, wildCard, reversed, iterate, null);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents left side starts with operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents contains operation.</returns>
        public static StringExpression LeftSideStartsWith(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.LeftSideStartsWith, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents contains operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents contains operation.</returns>
        public static StringExpression Contains(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.Contains, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents ends with operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents ends with operation.</returns>
        public static StringExpression EndsWith(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.EndsWith, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="BinaryExpression"/> that represents an equality comparison.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <returns>A <see cref="BinaryExpression"/> that represents an equality comparison.</returns>
        public static BinaryExpression Equals(FieldName fieldName, int? componentIndex, object value)
        {
            return new BinaryExpression(BinaryOperator.Equal, fieldName, componentIndex, value);
        }

        public static BinaryExpression GreaterThan(FieldName fieldName, int? componentIndex, object value)
        {
            return new BinaryExpression(BinaryOperator.GreaterThan, fieldName, componentIndex, value);
        }

        public static BinaryExpression GreaterThanOrEqual(FieldName fieldName, int? componentIndex, object value)
        {
            return new BinaryExpression(BinaryOperator.GreaterThanOrEqual, fieldName, componentIndex, value);
        }

        public static BinaryExpression LessThan(FieldName fieldName, int? componentIndex, object value)
        {
            return new BinaryExpression(BinaryOperator.LessThan, fieldName, componentIndex, value);
        }

        public static BinaryExpression LessThanOrEqual(FieldName fieldName, int? componentIndex, object value)
        {
            return new BinaryExpression(BinaryOperator.LessThanOrEqual, fieldName, componentIndex, value);
        }

        /// <summary>
        /// Creates a <see cref="MissingFieldExpression"/> that represents a missing field.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <returns>A <see cref="MissingFieldExpression"/> that represents a missing field.</returns>
        public static MissingFieldExpression Missing(FieldName fieldName, int? componentIndex)
        {
            return new MissingFieldExpression(fieldName, componentIndex);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents not contains operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents not contains operation.</returns>
        public static StringExpression NotContains(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.NotContains, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents not ends with operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents not ends with operation.</returns>
        public static StringExpression NotEndsWith(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.NotEndsWith, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents not starts with operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents not starts with operation.</returns>
        public static StringExpression NotStartsWith(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.NotStartsWith, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="MultiaryExpression"/> that represents logical OR operation over <paramref name="expressions"/>.
        /// </summary>
        /// <param name="expressions">The expressions.</param>
        /// <returns>A <see cref="MultiaryExpression"/> that has <see cref="MultiaryOperator"/> of OR on all <paramref name="expressions"/>.</returns>
        public static MultiaryExpression Or(params Expression[] expressions)
        {
            return new MultiaryExpression(MultiaryOperator.Or, expressions);
        }

        /// <summary>
        /// Creates a <see cref="MultiaryExpression"/> that represents logical OR operation over <paramref name="expressions"/>.
        /// </summary>
        /// <param name="expressions">The expressions.</param>
        /// <returns>A <see cref="MultiaryExpression"/> that has <see cref="MultiaryOperator"/> of OR on all <paramref name="expressions"/>.</returns>
        public static MultiaryExpression Or(IReadOnlyList<Expression> expressions)
        {
            return new MultiaryExpression(MultiaryOperator.Or, expressions);
        }

        /// <summary>
        /// Creates a <see cref="UnionExpression"/> that represents the union of multiple operations over <paramref name="expressions"/>.
        /// </summary>
        /// <param name="unionOperator">The union operator.</param>
        /// <param name="expressions">The expressions.</param>
        /// <returns>A <see cref="UnionExpression"/> with a set of expressions which results must be kept together.</returns>
        public static UnionExpression Union(UnionOperator unionOperator, IReadOnlyList<Expression> expressions)
        {
            return new UnionExpression(unionOperator, expressions);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents starts with operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents starts with operation.</returns>
        public static StringExpression StartsWith(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.StartsWith, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="InExpression"/> that represents logical IN operation over <paramref name="values"/>.
        /// </summary>
        /// <typeparam name="T">Type of the value included in the expression.</typeparam>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="values">The value.</param>
        public static InExpression<T> In<T>(FieldName fieldName, int? componentIndex, IEnumerable<T> values)
        {
            return new InExpression<T>(fieldName, componentIndex, values);
        }

        /// <summary>
        /// Creates a <see cref="StringExpression"/> that represents string equals operation.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        /// <returns>A <see cref="StringExpression"/> that represents string equals operation.</returns>
        public static StringExpression StringEquals(FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            return new StringExpression(StringOperator.Equals, fieldName, componentIndex, value, ignoreCase);
        }

        /// <summary>
        /// Creates a <see cref="CompartmentSearchExpression"/> that represents a compartment search operation.
        /// </summary>
        /// <param name="compartmentType">The compartment type.</param>
        /// <param name="compartmentId">The compartment id.</param>
        /// <param name="filteredResourceTypes">Resource types to filter on</param>
        /// <returns>A <see cref="CompartmentSearchExpression"/> that represents a compartment search operation.</returns>
        public static CompartmentSearchExpression CompartmentSearch(string compartmentType, string compartmentId, params string[] filteredResourceTypes)
        {
            return new CompartmentSearchExpression(compartmentType, compartmentId, filteredResourceTypes);
        }

        /// <summary>
        /// Creates a <see cref="SmartCompartmentSearchExpression"/> that filters all results by the smart user's compartment.
        /// </summary>
        /// <param name="compartmentType">The compartment type.</param>
        /// <param name="compartmentId">The compartment id.</param>
        /// <param name="filteredResourceTypes">Resource types to filter on</param>
        /// <returns>A <see cref="CompartmentSearchExpression"/> that represents a compartment search operation.</returns>
        public static SmartCompartmentSearchExpression SmartCompartmentSearch(string compartmentType, string compartmentId, params string[] filteredResourceTypes)
        {
            return new SmartCompartmentSearchExpression(compartmentType, compartmentId, filteredResourceTypes);
        }

        public abstract TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context);

        /// <inheritdoc />
        public abstract override string ToString();

        /// <summary>
        /// Accumulates a "value-insensitive" hash code of this instance, meaning it ignores parameterizable values.
        /// For example, date=2013&amp;name=Smith and date=2014&amp;name=Trudeau would have the same hash code.
        /// </summary>
        /// <param name="hashCode">The HashCode instance to accumulate into</param>
        public abstract void AddValueInsensitiveHashCode(ref HashCode hashCode);

        /// <summary>
        /// Determines whether the given expression is equal to this instance, ignoring any parameterizable values.
        /// For example, date=2013&amp;name=Smith and date=2014&amp;name=Trudeau would be considered equal
        /// </summary>
        /// <param name="other">The expression to compare this instance to.</param>
        public abstract bool ValueInsensitiveEquals(Expression other);
    }
}
