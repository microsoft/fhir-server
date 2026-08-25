// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Evaluates FHIRPath expressions through the configured provider.
    /// </summary>
    public static class FhirPathExtensions
    {
        /// <summary>
        /// Evaluates an expression and returns its selected elements.
        /// </summary>
        public static IEnumerable<ITypedElement> Select(this ITypedElement input, string expression, EvaluationContext context = null)
            => Compile(input, expression).Select(input, context);

        /// <summary>
        /// Evaluates an expression and returns its single primitive value, or null when empty.
        /// </summary>
        public static object Scalar(this ITypedElement input, string expression, EvaluationContext context = null)
        {
            ITypedElement[] result = Compile(input, expression).Select(input, context).Take(2).ToArray();
            return result.Length == 0 ? null : result.Single().Value;
        }

        /// <summary>
        /// Returns true when the expression evaluates to true or empty.
        /// </summary>
        public static bool Predicate(this ITypedElement input, string expression, EvaluationContext context = null)
            => BooleanEval(Compile(input, expression).Select(input, context)) is not false;

        /// <summary>
        /// Returns true when the expression evaluates to true.
        /// </summary>
        public static bool IsTrue(this ITypedElement input, string expression, EvaluationContext context = null)
            => BooleanEval(Compile(input, expression).Select(input, context)) is true;

        /// <summary>
        /// Returns true when the expression evaluates to the supplied boolean.
        /// </summary>
        public static bool IsBoolean(this ITypedElement input, string expression, bool value, EvaluationContext context = null)
            => BooleanEval(Compile(input, expression).Select(input, context)) is bool result && result == value;

        private static ICompiledFhirPath Compile(ITypedElement input, string expression)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);
            return FhirPathProvider.Instance.Compile(expression);
        }

        private static bool? BooleanEval(IEnumerable<ITypedElement> elements)
        {
            ITypedElement[] result = elements.Take(2).ToArray();
            return result.Length switch
            {
                0 => null,
                1 when result[0].Value is bool value => value,
                _ => true,
            };
        }
    }
}
