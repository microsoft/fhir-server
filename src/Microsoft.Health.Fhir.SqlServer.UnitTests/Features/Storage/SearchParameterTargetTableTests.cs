// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterTargetTableTests
    {
        [Theory]
        [InlineData(SearchParamType.Token, "TokenSearchParam")]
        [InlineData(SearchParamType.Reference, "ReferenceSearchParam")]
        [InlineData(SearchParamType.Date, "DateTimeSearchParam")]
        [InlineData(SearchParamType.String, "StringSearchParam")]
        [InlineData(SearchParamType.Number, "NumberSearchParam")]
        [InlineData(SearchParamType.Quantity, "QuantitySearchParam")]
        [InlineData(SearchParamType.Uri, "UriSearchParam")]
        [InlineData(SearchParamType.Special, "Unsupported")]
        public void GivenSearchParameter_WhenMapped_ThenCorrectTableIsReturned(SearchParamType type, string expected)
        {
            // Arrange
            var parameter = new SearchParameterInfo("test", "test", type);

            // Act
            string target = SearchParameterTargetTable.Get(parameter);

            // Assert
            Assert.Equal(expected, target);
        }

        [Theory]
        [InlineData("_id")]
        [InlineData("_lastUpdated")]
        [InlineData("_type")]
        public void GivenResourceParameter_WhenMapped_ThenResourceIsReturned(string code)
        {
            // Arrange
            var parameter = new SearchParameterInfo(code, code, SearchParamType.Token);

            // Act
            string target = SearchParameterTargetTable.Get(parameter);

            // Assert
            Assert.Equal("Resource", target);
        }

        [Fact]
        public void GivenCompositeParameter_WhenMapped_ThenItsComponentTypesDetermineTheTable()
        {
            // Arrange
            var token = new SearchParameterInfo("token", "token", SearchParamType.Token);
            var quantity = new SearchParameterInfo("quantity", "quantity", SearchParamType.Quantity);
            var parameter = new SearchParameterInfo("composite", "composite", SearchParamType.Composite, new Uri("http://example.org/composite"), new[]
            {
                new SearchParameterComponentInfo { ResolvedSearchParameter = token },
                new SearchParameterComponentInfo { ResolvedSearchParameter = quantity },
            });

            // Act
            string target = SearchParameterTargetTable.Get(parameter);

            // Assert
            Assert.Equal("TokenQuantityCompositeSearchParam", target);
        }
    }
}
