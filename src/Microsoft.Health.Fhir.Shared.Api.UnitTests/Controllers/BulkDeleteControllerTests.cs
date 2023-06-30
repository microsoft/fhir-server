// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class BulkDeleteControllerTests
    {
        [Fact]
        public async Task GivenRequestForPurgeHistory_WhenHardDeleteIsNotIncluded_ThenBadRequestIsReturned()
        {
            var controller = new BulkDeleteController(Substitute.For<IMediator>(), Substitute.For<IUrlResolver>());
            await Assert.ThrowsAsync<RequestNotValidException>(async () => await controller.BulkDelete(false, true, false));
        }
    }
}
