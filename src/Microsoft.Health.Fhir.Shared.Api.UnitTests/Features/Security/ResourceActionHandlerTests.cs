// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Health.Fhir.Api.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    public class ResourceActionHandlerTests
    {
        private readonly IAuthorizationPolicy _authorizationPolicy = Substitute.For<IAuthorizationPolicy>();
        private readonly AuthorizationHandlerContext _authorizationHandlerContext = new AuthorizationHandlerContext(new List<IAuthorizationRequirement> { ResourceActionRequirement }, new ClaimsPrincipal(), null);
        private readonly ResourceActionHandler _resourceActionHandler;

        private static readonly ResourceActionRequirement ResourceActionRequirement = new ResourceActionRequirement("Read");

        public ResourceActionHandlerTests()
        {
            _resourceActionHandler = new ResourceActionHandler(_authorizationPolicy);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async void GivenAReadResourceRequest_WhenAuthorizationHandlerHandles_ThenTheAuthorizationHandlerAppropriateStatus(bool authorized)
        {
            _authorizationPolicy.HasPermission(Arg.Any<ClaimsPrincipal>(), ResourceAction.Read).ReturnsForAnyArgs(authorized);

            await _resourceActionHandler.HandleAsync(_authorizationHandlerContext);

            Assert.Equal(authorized, _authorizationHandlerContext.HasSucceeded);
        }
    }
}
