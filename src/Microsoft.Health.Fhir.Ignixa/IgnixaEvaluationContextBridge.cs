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

            return Create(input.ToIgnixaElement(), ToFirelySemanticElement(context.Resource), ToFirelySemanticElement(context.RootResource), context);
        }

        /// <summary>
        /// Determines whether <paramref name="input"/> is a native Ignixa resource root that can be evaluated without a
        /// <see cref="ScopedNode"/>. This is taken automatically when the context Resource wraps that root, as
        /// the search indexer and <c>ResourceElement</c> provide, and RootResource is absent, the same root, or a
        /// <see cref="ScopedNode"/> over it. Descendants selected from the unscoped focus carry no
        /// <see cref="ScopedNode"/> ancestry, so callers must pass that context when they re-evaluate them.
        /// </summary>
        /// <param name="input">The element passed to the compiled expression.</param>
        /// <param name="context">The caller-supplied Firely context, or <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if <paramref name="input"/> can be evaluated without a <see cref="ScopedNode"/>.</returns>
        internal bool IsIndexedResourceRoot(ITypedElement input, FirelyEvaluationContext context)
        {
            IElement root = input is TypedElementAdapter ? input.ToIgnixaElement() : null;
            return root is not (null or IgnixaElementAdapter)
                && string.Equals(root.Location, root.InstanceType, StringComparison.Ordinal)
                && _schemaContext.IsResourceType(root.InstanceType)
                && ReferenceEquals(context?.Resource?.ToIgnixaElement(), root)
                && (context.RootResource is null
                    || ReferenceEquals(context.RootResource.ToIgnixaElement(), root)
                    || (context.RootResource is ScopedNode scopedRoot && ReferenceEquals(scopedRoot.Current.ToIgnixaElement(), root)));
        }

        /// <summary>
        /// Creates an Ignixa context for an input accepted by <see cref="IsIndexedResourceRoot"/>.
        /// Sets <paramref name="context"/> RootResource to a <see cref="ScopedNode"/> over <paramref name="originalInput"/>
        /// when it is null, as the scoped path does. Resource, and RootResource when it is the same root, are bound to
        /// <paramref name="contextNode"/>.
        /// </summary>
        /// <param name="originalInput">The element passed to the compiled expression.</param>
        /// <param name="contextNode">The Ignixa element the expression is evaluated on.</param>
        /// <param name="context">The caller-supplied Firely context.</param>
        /// <returns>The translated context.</returns>
        internal EvaluationContext CreateForIndexedResourceRoot(ITypedElement originalInput, IElement contextNode, FirelyEvaluationContext context)
        {
            context.RootResource ??= originalInput.ToScopedNode();
            IElement rootResource = context.RootResource is ScopedNode ? ToFirelySemanticElement(context.RootResource) : contextNode;

            return Create(contextNode, contextNode, rootResource, context);
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
                    ? reference => ToFirelySemanticElement(fhirContext.ElementResolver(reference))
                    : null,
            };

            foreach (KeyValuePair<string, IEnumerable<ITypedElement>> variable in context.Environment)
            {
                result = result with
                {
                    Environment = result.Environment.SetItem(
                        variable.Key,
                        variable.Value.Select(ToFirelySemanticElement).ToImmutableList()),
                };
            }

            return result;
        }

        private static IElement ToFirelySemanticElement(ITypedElement element)
            => element switch
            {
                null => null,
                TypedElementAdapter => new IgnixaElementAdapter(element),
                _ => element.ToIgnixaElement(),
            };

        private static ScopedNode GetResource(ScopedNode input)
            => input.AtResource ? input : input.ParentResource;

        private static ScopedNode GetRootResource(ScopedNode input)
        {
            ScopedNode resource = input.AtResource ? input : input.ParentResource;
            return resource?.Name == "contained" ? resource.ParentResource : resource;
        }
    }
}
