// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [Trait(Traits.Category, Categories.Bundle)]
    public sealed class IHeaderDictionaryExtensionsTests
    {
        [Fact]
        public void Clone_WhenHeaderIsProvided_CreatePerfectClone()
        {
            IHeaderDictionary headers = new HeaderDictionary()
            {
                { "x", "x" },
                { "y", "y" },
            };

            IHeaderDictionary clone = headers.Clone();

            Assert.False(ReferenceEquals(headers, clone));

            Assert.Equal(headers.Count, clone.Count);
            foreach (var keyValue in headers)
            {
                Assert.Equal(headers[keyValue.Key], clone[keyValue.Key]);
            }
        }
    }
}
