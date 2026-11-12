// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Creates <see cref="ReferenceSearchValue"/> instances for tests that are not exercising id admission.
    /// </summary>
    public static class TestReferenceSearchValues
    {
        /// <summary>
        /// Creates a resource reference whose id is validated by <paramref name="policy"/>, or by the extended policy when none is supplied.
        /// </summary>
        /// <param name="kind">The kind of reference.</param>
        /// <param name="baseUri">The base URI.</param>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="resourceId">The resource id.</param>
        /// <param name="policy">The resource id policy.</param>
        /// <returns>The reference search value.</returns>
        public static ReferenceSearchValue Resource(ReferenceKind kind, Uri baseUri, string resourceType, string resourceId, ResourceIdPolicy policy = null)
            => ReferenceSearchValue.FromResource(kind, baseUri, resourceType, BoundedResourceId.Parse(resourceId, policy ?? ResourceIdPolicy.Extended));
    }
}
