// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ActionConstraints;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.ActionConstraints
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConditionalOperations)]
    public class ConditionalConstraintAttributeTests
    {
        private readonly ConditionalConstraintAttribute _attribute;

        public ConditionalConstraintAttributeTests()
        {
            _attribute = new ConditionalConstraintAttribute();
        }

        [Theory]
        [InlineData("value", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void GivenIfNoneExistHeader_WhenAccepting_ThenAttributeShouldReturnCorrectValue(
            string headerValue,
            bool expected)
        {
            var headers = Substitute.For<IHeaderDictionary>();
            headers[KnownHeaders.IfNoneExist].Returns(new StringValues(headerValue));

            var httpRequest = Substitute.For<HttpRequest>();
            httpRequest.Headers.Returns(headers);

            var httpContext = Substitute.For<HttpContext>();
            httpContext.Request.Returns(httpRequest);

            var context = new ActionConstraintContext()
            {
                RouteContext = new RouteContext(httpContext),
            };

            Assert.Equal(expected, _attribute.Accept(context));
        }
    }
}
