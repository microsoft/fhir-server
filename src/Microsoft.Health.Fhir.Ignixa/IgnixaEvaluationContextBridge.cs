// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
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
        private readonly Func<InstanceCreationRequest, IElement> _instanceCreator;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaEvaluationContextBridge"/> class.
        /// </summary>
        /// <param name="schemaContext">The active FHIR schema.</param>
        public IgnixaEvaluationContextBridge(IgnixaSchemaContext schemaContext)
        {
            _schemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));

            // Ignixa delegates FHIRPath instance selectors (e.g. "Coding { code: 'x' }") to the host.
            _instanceCreator = new SourceNodeInstanceFactory(_schemaContext.Schema).Create;
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

            return Create(input.ToIgnixaElement(), context.Resource?.ToIgnixaElement(), context.RootResource?.ToIgnixaElement(), context);
        }

        /// <summary>
        /// Creates an Ignixa context for a native resource root accepted by <see cref="TryGetNativeRoot"/>, populating the caller
        /// context as <see cref="Create(ScopedNode, FirelyEvaluationContext)"/> does.
        /// </summary>
        /// <param name="originalInput">The element passed to the compiled expression.</param>
        /// <param name="root">The native resource root wrapped by <paramref name="originalInput"/>.</param>
        /// <param name="context">The caller-supplied Firely context.</param>
        /// <returns>The translated context.</returns>
        internal EvaluationContext CreateNative(ITypedElement originalInput, IElement root, FirelyEvaluationContext context)
        {
            context ??= new FirelyEvaluationContext();

            context.Resource ??= originalInput;
            context.RootResource ??= originalInput;

            return Create(root, root, root, context);
        }

        private IgnixaFhirEvaluationContext Create(IElement contextNode, IElement resource, IElement rootResource, FirelyEvaluationContext context)
        {
            IgnixaFhirEvaluationContext result = new()
            {
                Schema = _schemaContext.Schema,
                InstanceCreator = _instanceCreator,
                ContextNode = contextNode,
                Resource = resource,
                RootResource = rootResource,
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

        /// <summary>
        /// Determines whether <paramref name="input"/> wraps a native Ignixa resource root that can be evaluated without a
        /// <see cref="ScopedNode"/>, and whether the caller context, if populated, refers to that same root.
        /// </summary>
        /// <param name="input">The element passed to the compiled expression.</param>
        /// <param name="context">The caller-supplied Firely context.</param>
        /// <param name="root">The native root when this method returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> if the native root can be evaluated directly.</returns>
        internal bool TryGetNativeRoot(ITypedElement input, FirelyEvaluationContext context, out IElement root)
        {
            root = input is TypedElementAdapter ? input.ToIgnixaElement() : null;
            return root is not (null or IgnixaElementAdapter)
                && string.Equals(root.Location, root.InstanceType, StringComparison.Ordinal)
                && _schemaContext.IsResourceType(root.InstanceType)
                && IsAbsentOrSameRoot(context?.Resource, root)
                && IsAbsentOrSameRoot(context?.RootResource, root);
        }

        private static bool IsAbsentOrSameRoot(ITypedElement contextElement, IElement root)
            => contextElement is null || ReferenceEquals(contextElement.ToIgnixaElement(), root);

        private static ScopedNode GetResource(ScopedNode input)
            => input.AtResource ? input : input.ParentResource;

        private static ScopedNode GetRootResource(ScopedNode input)
        {
            ScopedNode resource = input.AtResource ? input : input.ParentResource;
            return resource?.Name == "contained" ? resource.ParentResource : resource;
        }
    }
}
