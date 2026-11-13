// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a reference search value.
    /// </summary>
    public class ReferenceSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceSearchValue"/> class.
        /// </summary>
        /// <param name="referenceKind">The kind of reference.</param>
        /// <param name="baseUri">The base URI of the resource.</param>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="resourceId">The resource id.</param>
        public ReferenceSearchValue(ReferenceKind referenceKind, Uri baseUri, string resourceType, string resourceId)
        {
            if (baseUri != null)
            {
                EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            }

            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));
            ModelInfoProvider.EnsureValidResourceType(resourceType, nameof(resourceType));

            Kind = referenceKind;
            BaseUri = baseUri;
            ResourceType = resourceType;
            ResourceId = resourceId;
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
        /// Gets the resource id.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Gets the length of the referenced resource id when it is longer than the maximum resource id length; otherwise <c>null</c>.
        /// Only the first maximum-length characters of such an id are indexed and compared, so the reference can match
        /// a different resource whose id starts with the same characters.
        /// </summary>
        /// <remarks>
        /// This is diagnostic information only. It is not part of equality, because it doesn't change what is indexed.
        /// </remarks>
        public int? OverlongResourceIdLength { get; init; }

        /// <summary>
        /// Gets the maximum resource id length that an overlong id was truncated to when it was parsed, or <c>null</c> when
        /// <see cref="OverlongResourceIdLength"/> is <c>null</c>.
        /// </summary>
        public int? MaxResourceIdLength { get; init; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

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
