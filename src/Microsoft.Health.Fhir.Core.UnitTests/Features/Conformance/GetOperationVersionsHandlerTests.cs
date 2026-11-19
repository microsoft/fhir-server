// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class GetOperationVersionsHandlerTests
    {
        [Fact]
        public void GivenADerivedHandler_WhenHandleIsInvoked_ThenSupportedVersionIsReturned()
        {
            var modelInfoProvider = Substitute.For<IModelInfoProvider>();
            modelInfoProvider.SupportedVersion.Returns(new VersionInfo("4.0.1"));
            var handler = new DerivedGetOperationVersionsHandler(modelInfoProvider);

            GetOperationVersionsResponse response = handler.InvokeHandle(new GetOperationVersionsRequest());

            Assert.Equal("4.0", response.DefaultVersion);
            Assert.Equal(new[] { "4.0" }, response.SupportedVersions);
        }

        private sealed class DerivedGetOperationVersionsHandler : GetOperationVersionsHandler
        {
            public DerivedGetOperationVersionsHandler(IModelInfoProvider provider)
                : base(provider)
            {
            }

            public GetOperationVersionsResponse InvokeHandle(GetOperationVersionsRequest request)
            {
                return Handle(request);
            }
        }
    }
}
