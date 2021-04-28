// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    public static class FhirRequestContextExtensions
    {
        public static RequestContextAccessor<IFhirRequestContext> SetupAccessor(this IFhirRequestContext context)
        {
            RequestContextAccessor<IFhirRequestContext> accessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            accessor.RequestContext.Returns(context);
            return accessor;
        }
    }
}
