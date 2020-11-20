// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a chained expression (where the child expression is chained to another resource.)
    /// </summary>
    public class ChainedExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChainedExpression"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type that supports this search expression.</param>
        /// <param name="referenceSearchParameter">The search parameter that establishes the reference</param>
        /// <param name="targetResourceType">The target resource type.</param>
        /// <param name="reversed">If this is a reversed chained expression.</param>
        /// <param name="expression">The search expression.</param>
        /// <remarks>
        /// <para>
        /// Let's say we looking for DiagnosticReports (https://www.hl7.org/fhir/diagnosticreport.html)
        /// with subject (where subject is type Patient) name is equal to Smith.
        /// <paramref name="resourceType"/> in this case is 'DiagnosticReport'.
        /// <paramref name="referenceSearchParameter"/> is 'subject'.
        /// <paramref name="targetResourceType"/> is 'Patient'.
        /// <paramref name="expression"/> is 'name is equal to Smith'.
        /// <paramref name="reversed"/> is false.
        /// In this case chained search allow you to walk through content of object and perform search on it.
        /// </para>
        /// <para>
        /// Reverse chained search is a bit different beast. Instead of going into content of object, it allow you to search based on who is referencing this object.
        /// Let's say we want to find all Patients which are part of Diagnostic report as subject and status of Diagnostic report is final.
        /// In this case Chained expression would look like this:
        /// <paramref name="resourceType"/> would be 'Patient'.
        /// <paramref name="referenceSearchParameter"/> would be 'subject'
        /// <paramref name="targetResourceType"/> is `DiagnosticReport`
        /// <paramref name="expression"/> is 'status is equal to final'.
        /// <paramref name="reversed"/> is true.
        /// </para>
        /// <para>
        /// Since <paramref name="expression"/> is abstract, it's possible to have nested chained expressions.
        /// There is no validation, but order (<paramref name="reversed"/>) of nested chained expression should be the same.
        /// <paramref name="resourceType"/> in nested expression should be same as <paramref name="targetResourceType"/> in parent one.
        /// </para>
        /// </remarks>
        public ChainedExpression(
            string resourceType,
            SearchParameterInfo referenceSearchParameter,
            string targetResourceType,
            bool reversed,
            Expression expression)
        {
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(resourceType), nameof(resourceType));
            EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownResource(targetResourceType), nameof(targetResourceType));
            EnsureArg.IsNotNull(expression, nameof(expression));

            ResourceType = resourceType;
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceType = targetResourceType;
            Reversed = reversed;
            Expression = expression;
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the target resource type.
        /// </summary>
        public string TargetResourceType { get; }

        /// <summary>
        /// Get if the expression is reversed.
        /// </summary>
        public bool Reversed { get; }

        /// <summary>
        /// Gets the search expression.
        /// </summary>
        public Expression Expression { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitChained(this, context);
        }

        public override string ToString()
        {
            return $"({(Reversed ? "Reverse " : string.Empty)}Chain {ReferenceSearchParameter.Name}:{TargetResourceType} {Expression})";
        }
    }
}
