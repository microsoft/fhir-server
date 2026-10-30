// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using FirelyEvaluationContext = Hl7.FhirPath.EvaluationContext;
using IgnixaEvaluationContext = Ignixa.FhirPath.Evaluation.EvaluationContext;

namespace Microsoft.Health.Fhir.Ignixa
{
    /// <summary>
    /// Executes an Ignixa-compiled FHIRPath expression.
    /// </summary>
    public sealed class IgnixaCompiledFhirPath : ICompiledFhirPath
    {
        private readonly Expression _expression;
        private readonly FhirPathEvaluator _evaluator;
        private readonly Func<IElement, IgnixaEvaluationContext, IEnumerable<IElement>> _compiledDelegate;
        private readonly IgnixaEvaluationContextBridge _contextBridge;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaCompiledFhirPath"/> class.
        /// </summary>
        /// <param name="expression">The source expression.</param>
        /// <param name="contextBridge">The evaluation-context bridge.</param>
        public IgnixaCompiledFhirPath(string expression, IgnixaEvaluationContextBridge contextBridge)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            _contextBridge = contextBridge ?? throw new ArgumentNullException(nameof(contextBridge));
            _expression = new FhirPathParser(preserveTrivia: false).Parse(expression);
            _evaluator = new FhirPathEvaluator();
            _compiledDelegate = new FhirPathDelegateCompiler(_evaluator).TryCompile(_expression);
        }

        /// <inheritdoc />
        public string Expression { get; }

        /// <inheritdoc />
        public IEnumerable<ITypedElement> Select(ITypedElement input, FirelyEvaluationContext context = null)
        {
            ArgumentNullException.ThrowIfNull(input);

            ScopedNode scopedInput = input.ToScopedNode();
            IElement ignixaInput = scopedInput.ToIgnixaElement();
            IgnixaEvaluationContext ignixaContext = _contextBridge.Create(scopedInput, context);
            IEnumerable<IElement> result = _compiledDelegate is null
                ? _evaluator.Evaluate(ignixaInput, _expression, ignixaContext)
                : _compiledDelegate(ignixaInput, ignixaContext);

            return result.ToTypedElements();
        }
    }
}
