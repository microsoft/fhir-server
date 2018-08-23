// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Routing;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Get
{
    public class GetCapabilitiesHandlerTests
    {
        [Fact]
        public async Task GivenARequestForCapabilityStatement_ThenACapabilityStatementIsReturned()
        {
            var provider = Substitute.For<IConformanceProvider>();
            var urlResolver = Substitute.For<IUrlResolver>();

            provider.GetCapabilityStatementAsync().Returns(new Hl7.Fhir.Model.CapabilityStatement
            {
                Publisher = "Microsoft Corporation",
            });

            var handler = new GetCapabilitiesHandler(provider, Substitute.For<ISystemConformanceProvider>(), new FhirJsonParser(), urlResolver);

            var response = await handler.Handle(
                new Messages.Get.GetCapabilitiesRequest(),
                CancellationToken.None);

            await provider.Received().GetCapabilityStatementAsync();
            Assert.NotNull(response.CapabilityStatement);
            Assert.Equal("Microsoft Corporation", response.CapabilityStatement.Publisher);
        }
    }
}
