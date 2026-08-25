// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Executes a Firely-compiled FHIRPath expression.
    /// </summary>
    public sealed class FirelyCompiledFhirPath : ICompiledFhirPath
    {
        private readonly CompiledExpression _compiledExpression;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirelyCompiledFhirPath"/> class.
        /// </summary>
        /// <param name="expression">The source expression.</param>
        /// <param name="compiledExpression">The Firely compiled delegate.</param>
        public FirelyCompiledFhirPath(string expression, CompiledExpression compiledExpression)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            _compiledExpression = compiledExpression ?? throw new ArgumentNullException(nameof(compiledExpression));
        }

        /// <inheritdoc />
        public string Expression { get; }

        /// <inheritdoc />
        public IEnumerable<ITypedElement> Select(ITypedElement input, EvaluationContext context = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            return _compiledExpression(input.ToScopedNode(), context ?? new EvaluationContext());
        }
    }
}
