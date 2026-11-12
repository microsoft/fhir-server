// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Creates the production <see cref="IResourceIdentityValidator"/> for tests without a request context.
    /// </summary>
    public static class TestResourceIdentityValidators
    {
        /// <summary>
        /// Creates a <see cref="ResourceIdentityValidator"/> backed by the production reference parser.
        /// </summary>
        /// <param name="policy">The resource id policy, or the standard policy when none is supplied.</param>
        /// <returns>The validator.</returns>
        public static IResourceIdentityValidator Create(ResourceIdPolicy policy = null)
        {
            policy ??= ResourceIdPolicy.Standard;
            return new ResourceIdentityValidator(
                policy,
                new ReferenceSearchValueParser(new FhirRequestContextAccessor(), new FhirServerInstanceConfiguration(), policy));
        }
    }
}
