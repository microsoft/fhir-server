// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a reference search value whose payload is either a policy-validated resource id or a complete literal.
    /// </summary>
    public class ReferenceSearchValue : ISearchValue
    {
        private readonly BoundedResourceId _id;
        private readonly string _literal;

        private ReferenceSearchValue(ReferenceKind referenceKind, Uri baseUri, string resourceType, BoundedResourceId id, string literal)
        {
            if (baseUri != null)
            {
                EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            }

            ModelInfoProvider.EnsureValidResourceType(resourceType, nameof(resourceType));

            Kind = referenceKind;
            BaseUri = baseUri;
            ResourceType = resourceType;
            _id = id;
            _literal = literal;
        }

        /// <summary>
        /// Gets the kind of reference.
        /// </summary>
        public ReferenceKind Kind { get; }

        /// <summary>
        /// Gets the base URI of the resource.
        /// </summary>
        public Uri BaseUri { get; }

        /// <summary>
        /// Gets the resource type.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the complete resource id or literal payload.
        /// </summary>
        public string ResourceId => _id?.Value ?? _literal;

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

        /// <summary>
        /// Creates a reference to a resource whose id has been validated by the resource id policy.
        /// </summary>
        /// <param name="referenceKind">The kind of reference.</param>
        /// <param name="baseUri">The base URI of the resource.</param>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="id">The validated resource id.</param>
        /// <returns>A reference search value with a bounded resource id payload.</returns>
        public static ReferenceSearchValue FromResource(ReferenceKind referenceKind, Uri baseUri, string resourceType, BoundedResourceId id)
        {
            EnsureArg.IsNotNull(id, nameof(id));

            return new ReferenceSearchValue(referenceKind, baseUri, resourceType, id, literal: null);
        }

        /// <summary>
        /// Creates a reference whose payload is a complete literal that is not interpreted as a resource id.
        /// </summary>
        /// <param name="exactValue">The complete literal value.</param>
        /// <returns>A reference search value with a literal payload.</returns>
        public static ReferenceSearchValue FromLiteral(string exactValue)
        {
            EnsureArg.IsNotNullOrWhiteSpace(exactValue, nameof(exactValue));

            return new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null, resourceType: null, id: null, literal: exactValue);
        }

        /// <summary>
        /// Returns a copy with a different matching context, preserving the complete id or literal payload.
        /// </summary>
        /// <param name="referenceKind">The kind of reference.</param>
        /// <param name="baseUri">The base URI of the resource.</param>
        /// <param name="resourceType">The resource type.</param>
        /// <returns>A reference search value with the same payload.</returns>
        public ReferenceSearchValue WithMatchingContext(ReferenceKind referenceKind, Uri baseUri, string resourceType)
        {
            return new ReferenceSearchValue(referenceKind, baseUri, resourceType, _id, _literal);
        }

        /// <inheritdoc />
        public void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }

        public bool Equals([AllowNull] ISearchValue other)
        {
            if (other == null)
            {
                return false;
            }

            var referenceSearchValueOther = other as ReferenceSearchValue;

            if (referenceSearchValueOther == null)
            {
                return false;
            }

            return Kind == referenceSearchValueOther.Kind &&
                   BaseUri == referenceSearchValueOther.BaseUri &&
                   string.Equals(ResourceType, referenceSearchValueOther.ResourceType, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ResourceId, referenceSearchValueOther.ResourceId, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (BaseUri != null)
            {
                return $"{BaseUri}{ResourceType}/{ResourceId}";
            }
            else if (string.IsNullOrWhiteSpace(ResourceType))
            {
                return ResourceId;
            }

            return $"{ResourceType}/{ResourceId}";
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Kind.GetHashCode(),
                BaseUri != null ? BaseUri.GetHashCode() : 0,
                ResourceType != null ? ResourceType.GetHashCode(StringComparison.OrdinalIgnoreCase) : 0,
                ResourceId != null ? ResourceId.GetHashCode(StringComparison.OrdinalIgnoreCase) : 0);
        }
    }
}
