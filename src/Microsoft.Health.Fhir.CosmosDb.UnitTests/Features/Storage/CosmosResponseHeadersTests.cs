// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosResponseHeadersTests
    {
        [Fact]
        public void Create_WhenHeadersProvided_CopiesAllNamedProperties()
        {
            Headers mockHeaders = Substitute.ForPartsOf<Headers>();
            mockHeaders.ActivityId.Returns("test-activity-id");
            mockHeaders.ContentLength.Returns("1024");
            mockHeaders.ContentType.Returns("application/json");
            mockHeaders.ContinuationToken.Returns("continuation-token-123");
            mockHeaders.ETag.Returns("\"etag-value\"");
            mockHeaders.Location.Returns("https://example.com/resource");
            mockHeaders.RequestCharge.Returns(5.5);
            mockHeaders.Session.Returns("session-token");
            mockHeaders.AllKeys().Returns(new string[] { "x-ms-substatus" });
            mockHeaders.Get("x-ms-substatus").Returns("1002");

            // Act
            CosmosResponseHeaders result = CosmosResponseHeaders.Create(mockHeaders);

            // Assert
            Assert.Equal("test-activity-id", result.ActivityId);
            Assert.Equal(1002, result.SubStatusValue);
            Assert.Equal("1024", result.ContentLength);
            Assert.Equal("application/json", result.ContentType);
            Assert.Equal("continuation-token-123", result.ContinuationToken);
            Assert.Equal("\"etag-value\"", result.ETag);
            Assert.Equal("https://example.com/resource", result.Location);
            Assert.Equal(5.5, result.RequestCharge);
            Assert.Equal("session-token", result.Session);
        }

        [Fact]
        public void Create_WhenHeadersHaveDefaultValues_CopiesDefaults()
        {
            // Arrange
            Headers headers = new Headers();

            // Act
            CosmosResponseHeaders result = CosmosResponseHeaders.Create(headers);

            // Assert
            Assert.Null(result.ActivityId);
            Assert.Null(result.SubStatusValue);
            Assert.Null(result.ContentLength);
            Assert.Null(result.ContentType);
            Assert.Null(result.ContinuationToken);
            Assert.Null(result.ETag);
            Assert.Null(result.Location);
            Assert.Equal(0, result.RequestCharge);
            Assert.Null(result.Session);
            Assert.Null(result["x-ms-substatus"]);
        }

        [Fact]
        public void Create_WhenCustomHeadersProvided_HeadersAccessibleByIndexer()
        {
            // Arrange
            Headers headers = new Headers
            {
                { "x-custom-header", "custom-value" },
                { "x-another-header", "another-value" },
            };

            // Act
            CosmosResponseHeaders result = CosmosResponseHeaders.Create(headers);

            // Assert
            Assert.Equal("custom-value", result["x-custom-header"]);
            Assert.Equal("another-value", result["x-another-header"]);
        }

        [Fact]
        public void Indexer_WhenHeaderDoesNotExist_ReturnsNull()
        {
            // Arrange
            Headers headers = new Headers();
            CosmosResponseHeaders result = CosmosResponseHeaders.Create(headers);

            // Act
            string value = result["non-existent-header"];

            // Assert
            Assert.Null(value);
        }

        [Fact]
        public void Indexer_WhenSettingNewHeader_StoresValue()
        {
            // Arrange
            Headers headers = new Headers();
            CosmosResponseHeaders result = CosmosResponseHeaders.Create(headers);

            // Act
            result["new-header"] = "new-value";

            // Assert
            Assert.Equal("new-value", result["new-header"]);
        }

        [Fact]
        public void Indexer_WhenOverwritingExistingHeader_UpdatesValue()
        {
            // Arrange
            Headers headers = new Headers
            {
                { "x-header", "original-value" },
            };
            CosmosResponseHeaders result = CosmosResponseHeaders.Create(headers);

            // Act
            result["x-header"] = "updated-value";

            // Assert
            Assert.Equal("updated-value", result["x-header"]);
        }

        [Fact]
        public void Create_WhenSubStatusHeaderPresent_ParsesSubStatusValue()
        {
            // Arrange
            Headers headers = new Headers
            {
                { "x-ms-substatus", "3200" },
            };

            // Act
            CosmosResponseHeaders result = CosmosResponseHeaders.Create(headers);

            // Assert
            Assert.Equal(3200, result.SubStatusValue);
            Assert.Equal("3200", result["x-ms-substatus"]);
        }
    }
}
