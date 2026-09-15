// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using FirelyEvaluationContext = Hl7.FhirPath.EvaluationContext;
using FirelyFhirEvaluationContext = Hl7.Fhir.FhirPath.FhirEvaluationContext;
using IgnixaFhirEvaluationContext = Ignixa.FhirPath.Evaluation.FhirEvaluationContext;

namespace Microsoft.Health.Fhir.Ignixa
{
    /// <summary>
    /// Translates Firely evaluation state to the Ignixa evaluation model.
    /// </summary>
    public sealed class IgnixaEvaluationContextBridge
    {
        private readonly IgnixaSchemaContext _schemaContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaEvaluationContextBridge"/> class.
        /// </summary>
        /// <param name="schemaContext">The active FHIR schema.</param>
        public IgnixaEvaluationContextBridge(IgnixaSchemaContext schemaContext)
        {
            _schemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        }

        /// <summary>
        /// Creates an Ignixa context that preserves Firely's scoped-node and resolver behavior.
        /// </summary>
        /// <param name="input">The scoped input element.</param>
        /// <param name="context">The caller-supplied Firely context.</param>
        /// <returns>The translated context.</returns>
        public EvaluationContext Create(ScopedNode input, FirelyEvaluationContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            context ??= new FirelyEvaluationContext();

            context.Resource ??= GetResource(input);
            context.RootResource ??= GetRootResource(input);
            var ignixaInput = input.ToIgnixaElement();

            IgnixaFhirEvaluationContext result = new()
            {
                Schema = _schemaContext.Schema,
                ContextNode = ignixaInput,
                Resource = context.Resource?.ToIgnixaElement(),
                RootResource = context.RootResource?.ToIgnixaElement(),
                ElementResolver = context is FirelyFhirEvaluationContext fhirContext && fhirContext.ElementResolver is not null
                    ? reference => fhirContext.ElementResolver(reference)?.ToIgnixaElement()
                    : null,
            };

            foreach (KeyValuePair<string, IEnumerable<ITypedElement>> variable in context.Environment)
            {
                result = result with
                {
                    Environment = result.Environment.SetItem(
                        variable.Key,
                        variable.Value.Select(element => element.ToIgnixaElement()).ToImmutableList()),
                };
            }

            return result;
        }

        private static ScopedNode GetResource(ScopedNode input)
            => input.AtResource ? input : input.ParentResource;

        private static ScopedNode GetRootResource(ScopedNode input)
        {
            ScopedNode resource = input.AtResource ? input : input.ParentResource;
            return resource?.Name == "contained" ? resource.ParentResource : resource;
        }
    }
}
