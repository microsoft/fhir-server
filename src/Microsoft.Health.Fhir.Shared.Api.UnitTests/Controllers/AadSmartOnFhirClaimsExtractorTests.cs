// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class AadSmartOnFhirClaimsExtractorTests
    {
        private const string ClientIdParameterName = "client_id";
        private const string DefaultFormClientId = "form";
        private const string DefaultQueryClientId = "query";

        private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        private readonly AadSmartOnFhirClaimsExtractor _claimsExtractor;

        private readonly HttpContext _httpContext = Substitute.For<HttpContext>();
        private readonly HttpRequest _request = Substitute.For<HttpRequest>();

        public AadSmartOnFhirClaimsExtractorTests()
        {
            _claimsExtractor = new AadSmartOnFhirClaimsExtractor(_httpContextAccessor);

            _request.Form = new FormCollection(new Dictionary<string, StringValues>()
            {
                { ClientIdParameterName, DefaultFormClientId },
                { "secret", "secret" },
            });

            _request.Query = new QueryCollection(new Dictionary<string, StringValues>()
            {
                { ClientIdParameterName, DefaultQueryClientId },
                { "secret", "secret" },
            });

            _httpContext.Request.Returns(_request);

            _httpContextAccessor.HttpContext.Returns(_httpContext);
        }

        [Fact]
        public void GivenARequestWithFormContentType_WhenExtracted_ThenCorrectClientIdShouldBeExtracted()
        {
            ExecuteAndValidate(true, DefaultFormClientId);
        }

        [Fact]
        public void GivenARequestWithNoFormContentType_WhenExtracted_ThenCorrectClientIdShouldBeExtracted()
        {
            ExecuteAndValidate(false, DefaultQueryClientId);
        }

        [Fact]
        public void GivenARequestWithMultipleClientIds_WhenExtracted_ThenCorrectClientIdsShouldBeExtracted()
        {
            const string client1 = "client1";
            const string client2 = "client2";

            _request.Form = new FormCollection(new Dictionary<string, StringValues>()
            {
                { ClientIdParameterName, new StringValues(new[] { client1, client2 }) },
            });

            ExecuteAndValidate(true, client1, client2);
        }

        private void ExecuteAndValidate(bool hasFormContentType, params object[] expectedClientIds)
        {
            _request.HasFormContentType.Returns(hasFormContentType);

            IReadOnlyCollection<KeyValuePair<string, string>> results = _claimsExtractor.Extract();

            Assert.Collection(
                results,
                expectedClientIds.Select(clientId => new Action<KeyValuePair<string, string>>(claim =>
                {
                    Assert.Equal(ClientIdParameterName, claim.Key);
                    Assert.Equal(clientId, claim.Value);
                })).ToArray());
        }
    }
}
