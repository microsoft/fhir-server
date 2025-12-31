// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Search.FhirPath
{
    /// <summary>
    /// Firely SDK-based compiled FHIRPath expression.
    /// </summary>
    internal sealed class FirelyCompiledFhirPath : ICompiledFhirPath
    {
        private readonly CompiledExpression _compiledExpression;

        public FirelyCompiledFhirPath(string expression, CompiledExpression compiledExpression)
        {
            EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));
            EnsureArg.IsNotNull(compiledExpression, nameof(compiledExpression));

            Expression = expression;
            _compiledExpression = compiledExpression;
        }

        /// <inheritdoc />
        public string Expression { get; }

        /// <inheritdoc />
        public IEnumerable<ITypedElement> Evaluate(ITypedElement element, EvaluationContext context = null)
        {
            EnsureArg.IsNotNull(element, nameof(element));

            return _compiledExpression.Invoke(element, context);
        }

        /// <inheritdoc />
        public T Scalar<T>(ITypedElement element, EvaluationContext context = null)
        {
            EnsureArg.IsNotNull(element, nameof(element));

            var results = Evaluate(element, context);
            var first = results.FirstOrDefault();

            if (first == null)
            {
                return default;
            }

            // Get the scalar value
            var value = first.Value;
            if (value is T typedValue)
            {
                return typedValue;
            }

            // Try conversion for common types
            if (value != null && typeof(T) == typeof(string))
            {
                return (T)(object)value.ToString();
            }

            return default;
        }

        /// <inheritdoc />
        public bool Predicate(ITypedElement element, EvaluationContext context = null)
        {
            EnsureArg.IsNotNull(element, nameof(element));

            var results = Evaluate(element, context).ToList();

            // FHIRPath predicate semantics:
            // - Empty collection = false
            // - Single boolean = that boolean value
            // - Non-empty collection of non-booleans = true
            if (results.Count == 0)
            {
                return false;
            }

            if (results.Count == 1 && results[0].Value is bool boolValue)
            {
                return boolValue;
            }

            // Non-empty collection = true (exists semantics)
            return true;
        }
    }
}
