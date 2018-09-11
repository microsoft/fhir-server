// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Context;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    public class FhirContextAccessorTests
    {
        [Fact]
        public async Task GivenAFhirContextAccessor_WhenOnDifferentAsyncThreads_TheFhirContextIsDifferent()
        {
            var fhirContextAccessor = new FhirContextAccessor();

            var thread1 = Task.Run(async () =>
            {
                fhirContextAccessor.FhirContext = new FhirContext(Guid.NewGuid().ToString());
                await Task.Delay(50);
                return fhirContextAccessor.FhirContext;
            });

            var thread2 = Task.Run(async () =>
            {
                fhirContextAccessor.FhirContext = new FhirContext(Guid.NewGuid().ToString());
                await Task.Delay(0);
                return fhirContextAccessor.FhirContext;
            });

            var correlationId1 = (await thread1).CorrelationId;
            var correlationId2 = (await thread2).CorrelationId;

            Assert.NotEqual(Guid.Empty.ToString(), correlationId1);
            Assert.NotEqual(Guid.Empty.ToString(), correlationId2);

            Assert.NotEqual(correlationId1, correlationId2);
        }
    }
}
