// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirRequestContextAccessorTests
    {
        [Fact]
        public async Task GivenAFhirRequestContextAccessor_WhenOnDifferentAsyncThreads_TheFhirContextIsDifferent()
        {
            var fhirRequestContextAccessor = new FhirRequestContextAccessor();

            var thread1 = Task.Run(async () =>
            {
                IFhirRequestContext fhirRequestContext = Substitute.For<IFhirRequestContext>();
                fhirRequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());

                fhirRequestContextAccessor.RequestContext = fhirRequestContext;
                await Task.Delay(50);
                return fhirRequestContextAccessor.RequestContext;
            });

            var thread2 = Task.Run(async () =>
            {
                IFhirRequestContext fhirRequestContext = Substitute.For<IFhirRequestContext>();
                fhirRequestContext.CorrelationId.Returns(Guid.NewGuid().ToString());

                fhirRequestContextAccessor.RequestContext = fhirRequestContext;
                await Task.Delay(0);
                return fhirRequestContextAccessor.RequestContext;
            });

            var correlationId1 = (await thread1).CorrelationId;
            var correlationId2 = (await thread2).CorrelationId;

            Assert.NotEqual(Guid.Empty.ToString(), correlationId1);
            Assert.NotEqual(Guid.Empty.ToString(), correlationId2);

            Assert.NotEqual(correlationId1, correlationId2);
        }
    }
}
