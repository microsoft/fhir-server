// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    public class FhirContextMiddlewareTests
    {
        [Fact]
        public async Task WhenExecutingFhirContextMiddleware_GivenAnHttpContext_TheFhirContextObjectShouldBeInitialized()
        {
            var fhirContextAccessor = Substitute.For<IFhirContextAccessor>();
            var fhirContextMiddlware = new FhirContextMiddleware(next: (innerHttpContext) => Task.CompletedTask);
            string Provider() => Guid.NewGuid().ToString();

            await fhirContextMiddlware.Invoke(new DefaultHttpContext(), fhirContextAccessor, Provider);

            Assert.NotNull(fhirContextAccessor.FhirContext);
            Assert.Equal(ValueSets.AuditEventType.RestFulOperation.System, fhirContextAccessor.FhirContext.RequestType.System);
            Assert.Equal(ValueSets.AuditEventType.RestFulOperation.Code, fhirContextAccessor.FhirContext.RequestType.Code);
        }
    }
}
