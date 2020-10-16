// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    public static class FhirRequestContextExtensions
    {
        public static IFhirRequestContextAccessor SetupAccessor(this IFhirRequestContext context)
        {
            IFhirRequestContextAccessor accessor = Substitute.For<IFhirRequestContextAccessor>();
            accessor.FhirRequestContext.Returns(context);
            return accessor;
        }
    }
}
