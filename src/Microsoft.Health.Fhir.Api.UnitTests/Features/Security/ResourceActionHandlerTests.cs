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
        private static readonly ResourceActionRequirement _resourceActionRequirement = new ResourceActionRequirement("Read");
        private readonly AuthorizationHandlerContext _authorizationHandlerContext = new AuthorizationHandlerContext(new List<IAuthorizationRequirement> { _resourceActionRequirement }, new ClaimsPrincipal(), null);

        [Fact]
        public void GivenAReadResourceRequest_WhenUnauthorized_ThenTheAuthorizationHandlerReturnsFalse()
        {
            _authorizationPolicy.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceAction>()).ReturnsForAnyArgs(false);
            var resourceActionHandler = new ResourceActionHandler(_authorizationPolicy);
            resourceActionHandler.HandleAsync(_authorizationHandlerContext).Wait();
            Assert.False(_authorizationHandlerContext.HasSucceeded);
        }

        [Fact]
        public void GivenAReadResourceRequest_WhenAuthorized_ThenTheAuthorizationHandlerReturnsTrue()
        {
            _authorizationPolicy.HasPermissionAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<ResourceAction>()).ReturnsForAnyArgs(true);
            var resourceActionHandler = new ResourceActionHandler(_authorizationPolicy);
            resourceActionHandler.HandleAsync(_authorizationHandlerContext).Wait();
            Assert.True(_authorizationHandlerContext.HasSucceeded);
        }
    }
}
