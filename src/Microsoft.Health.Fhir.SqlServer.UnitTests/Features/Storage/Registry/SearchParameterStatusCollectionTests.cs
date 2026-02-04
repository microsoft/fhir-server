// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage.Registry
{
    /// <summary>
    /// Unit tests for SearchParameterStatusCollection.
    /// Tests the custom IEnumerable&lt;SqlDataRecord&gt; implementation and data transformation logic.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterStatusCollectionTests
    {
        [Fact]
        public void GivenEmptyCollection_WhenEnumerated_ThenReturnsNoRecords()
        {
            // Arrange
            var collection = new SearchParameterStatusCollection();

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            Assert.Empty(records);
        }

        [Fact]
        public void GivenCollectionWithoutLastUpdated_WhenEnumerated_ThenGeneratesRecordsWithThreeColumns()
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
                LastUpdated = DateTimeOffset.UtcNow,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            Assert.Single(records);
            var record = records[0];
            Assert.Equal(3, record.FieldCount);
            Assert.Equal("Uri", record.GetName(0));
            Assert.Equal("Status", record.GetName(1));
            Assert.Equal("IsPartiallySupported", record.GetName(2));
        }

        [Fact]
        public void GivenCollectionWithLastUpdated_WhenEnumerated_ThenGeneratesRecordsWithFourColumns()
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: true);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
                LastUpdated = DateTimeOffset.UtcNow,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            Assert.Single(records);
            var record = records[0];
            Assert.Equal(4, record.FieldCount);
            Assert.Equal("Uri", record.GetName(0));
            Assert.Equal("Status", record.GetName(1));
            Assert.Equal("IsPartiallySupported", record.GetName(2));
            Assert.Equal("LastUpdated", record.GetName(3));
        }

        [Fact]
        public void GivenMultipleStatuses_WhenEnumerated_ThenAllRecordsAreGenerated()
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
            });
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = true,
            });
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-gender"),
                Status = SearchParameterStatus.Enabled,
                IsPartiallySupported = false,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            Assert.Equal(3, records.Count);
        }

        [Fact]
        public void GivenStatusWithUri_WhenEnumerated_ThenUriIsCorrectlySet()
        {
            // Arrange
            var expectedUri = "http://hl7.org/fhir/SearchParameter/Patient-name";
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri(expectedUri),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            var record = records[0];
            Assert.Equal(expectedUri, record.GetString(0));
        }

        [Theory]
        [InlineData(SearchParameterStatus.Supported, "Supported")]
        [InlineData(SearchParameterStatus.Enabled, "Enabled")]
        [InlineData(SearchParameterStatus.Disabled, "Disabled")]
        [InlineData(SearchParameterStatus.Unsupported, "Unsupported")]
        public void GivenDifferentStatuses_WhenEnumerated_ThenStatusStringIsCorrect(SearchParameterStatus status, string expectedString)
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = status,
                IsPartiallySupported = false,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            var record = records[0];
            Assert.Equal(expectedString, record.GetString(1));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenIsPartiallySupported_WhenEnumerated_ThenBooleanIsCorrectlySet(bool isPartiallySupported)
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = isPartiallySupported,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            var record = records[0];
            Assert.Equal(isPartiallySupported, record.GetSqlBoolean(2).Value);
        }

        [Fact]
        public void GivenValidLastUpdated_WhenEnumeratedWithLastUpdatedFlag_ThenDateTimeOffsetIsSet()
        {
            // Arrange
            var expectedLastUpdated = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
            var collection = new SearchParameterStatusCollection(includeLastUpdated: true);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
                LastUpdated = expectedLastUpdated,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            var record = records[0];
            Assert.False(record.IsDBNull(3));
            Assert.Equal(expectedLastUpdated, record.GetDateTimeOffset(3));
        }

        [Fact]
        public void GivenDefaultLastUpdated_WhenEnumeratedWithLastUpdatedFlag_ThenLastUpdatedIsNull()
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: true);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
                LastUpdated = default(DateTimeOffset),
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            var record = records[0];
            Assert.True(record.IsDBNull(3));
        }

        [Fact]
        public void GivenCollectionWithMixedLastUpdatedValues_WhenEnumerated_ThenNullsAreHandledCorrectly()
        {
            // Arrange
            var validLastUpdated = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
            var collection = new SearchParameterStatusCollection(includeLastUpdated: true);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
                LastUpdated = validLastUpdated,
            });
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = true,
                LastUpdated = default(DateTimeOffset),
            });

            // Act - Enumerate and extract data before the record is reused
            var extractedData = new List<(string Uri, string Status, bool IsPartial, bool LastUpdatedIsNull, DateTimeOffset? LastUpdated)>();
            foreach (var record in (IEnumerable<SqlDataRecord>)collection)
            {
                var uri = record.GetString(0);
                var status = record.GetString(1);
                var isPartial = record.GetSqlBoolean(2).Value;
                var lastUpdatedIsNull = record.IsDBNull(3);
                var lastUpdated = lastUpdatedIsNull ? (DateTimeOffset?)null : record.GetDateTimeOffset(3);
                extractedData.Add((uri, status, isPartial, lastUpdatedIsNull, lastUpdated));
            }

            // Assert
            Assert.Equal(2, extractedData.Count);

            // First record should have LastUpdated set
            Assert.False(extractedData[0].LastUpdatedIsNull);
            Assert.Equal(validLastUpdated, extractedData[0].LastUpdated);

            // Second record should have LastUpdated as null
            Assert.True(extractedData[1].LastUpdatedIsNull);
            Assert.Null(extractedData[1].LastUpdated);
        }

        [Fact]
        public void GivenCollectionUsedAsListOperations_WhenManipulated_ThenBehavesAsExpected()
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            var status1 = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
            };
            var status2 = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"),
                Status = SearchParameterStatus.Disabled,
                IsPartiallySupported = true,
            };

            // Act - Test List<T> functionality
            collection.Add(status1);
            collection.Add(status2);
            Assert.Equal(2, collection.Count);

            collection.Remove(status1);
            Assert.Single(collection);

            collection.Clear();
            Assert.Empty(collection);
        }

        [Fact]
        public void GivenLongUriValue_WhenEnumerated_ThenUriIsCorrectlyStored()
        {
            // Arrange - Test URI at boundary (128 char limit defined in SqlMetaData)
            var longUri = "http://hl7.org/fhir/SearchParameter/VeryLongSearchParameterNameThatIsExtremelyLongToTestBoundaryConditionsForTheUriColumn123";
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri(longUri),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
            });

            // Act
            var records = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert
            var record = records[0];
            Assert.Equal(longUri, record.GetString(0));
        }

        [Fact]
        public void GivenCollectionEnumeratedMultipleTimes_WhenCalled_ThenProducesConsistentResults()
        {
            // Arrange
            var collection = new SearchParameterStatusCollection(includeLastUpdated: false);
            collection.Add(new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = false,
            });

            // Act - Enumerate twice
            var records1 = ((IEnumerable<SqlDataRecord>)collection).ToList();
            var records2 = ((IEnumerable<SqlDataRecord>)collection).ToList();

            // Assert - Both enumerations should produce same count and values
            Assert.Equal(records1.Count, records2.Count);
            Assert.Equal(records1[0].GetString(0), records2[0].GetString(0));
            Assert.Equal(records1[0].GetString(1), records2[0].GetString(1));
        }
    }
}
