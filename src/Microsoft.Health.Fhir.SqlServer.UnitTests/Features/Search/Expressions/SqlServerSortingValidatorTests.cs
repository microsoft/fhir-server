// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSortingValidatorTests
    {
        private SqlServerSortingValidator _sqlServerSortingValidator;
        private SchemaInformation _schemaInformation;

        private SearchParameterInfo _lastUpdatedParamInfo = new SearchParameterInfo(name: "lastupdated", code: "lastupdated", SearchParamType.Date, SearchParameterNames.LastUpdatedUri);
        private SearchParameterInfo _resourceTypeParamInfo = new SearchParameterInfo(name: "type", code: "type", SearchParamType.String, SearchParameterNames.ResourceTypeUri);

        public SqlServerSortingValidatorTests()
        {
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Max, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
            _sqlServerSortingValidator = new SqlServerSortingValidator(_schemaInformation);
        }

        [Theory]
        [MemberData(nameof(GetSupportedSearchParamTypes))]
        public void GivenSupportedSortParametersType_WhenValidating_ThenReturnsTrue(SearchParamType searchParamType)
        {
            SearchParameterInfo paramInfo = new SearchParameterInfo(name: "paramName", code: "paramName", searchParamType);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (paramInfo, SortOrder.Ascending) },
            };

            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.True(sortingValid);
            Assert.Empty(errorMessage);
        }

        [Theory]
        [MemberData(nameof(GetSupportedSearchParamTypes))]
        public void GivenSupportedSortParametersTypeForSchemaOlderThanV17_WhenValidating_ThenReturnsFalse(SearchParamType searchParamType)
        {
            SearchParameterInfo paramInfo = new SearchParameterInfo(name: "paramName", code: "paramName", searchParamType);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (paramInfo, SortOrder.Ascending) },
            };

            _schemaInformation.Current = (int)SchemaVersion.V16;
            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Theory]
        [MemberData(nameof(GetUnsupportedSearchParamTypes))]
        public void GivenUnsupportedSortParametersType_WhenValidating_ThenReturnsFalse(SearchParamType searchParamType)
        {
            SearchParameterInfo paramInfo = new SearchParameterInfo(name: "paramName", code: "paramName", searchParamType);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (paramInfo, SortOrder.Ascending) },
            };

            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Fact]
        public void GivenMultipleSortParameters_WhenValidating_ThenReturnsFalse()
        {
            SearchParameterInfo dateParamInfo = new SearchParameterInfo(name: "birthdate", code: "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"));
            SearchParameterInfo stringParamInfo = new SearchParameterInfo(name: "name", code: "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (dateParamInfo, SortOrder.Ascending) },
                { (stringParamInfo, SortOrder.Descending) },
            };

            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Theory]
        [InlineData((int)SchemaVersion.V7)]
        [InlineData((int)SchemaVersion.V8)]
        public void GivenLastUpdatedAndResourceTypeSortForSchemaOlderThanV9_WhenValidating_ThenReturnsFalse(int schemaVersion)
        {
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (_resourceTypeParamInfo, SortOrder.Ascending) },
                { (_lastUpdatedParamInfo, SortOrder.Ascending) },
            };

            _schemaInformation.Current = schemaVersion;
            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Theory]
        [InlineData((int)SchemaVersion.V9)]
        [InlineData((int)SchemaVersion.V10)]
        public void GivenLastUpdatedAndResourceTypeSortForSchemaNewerThanV9_WhenValidating_ThenReturnsTrue(int schemaVersion)
        {
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (_resourceTypeParamInfo, SortOrder.Ascending) },
                { (_lastUpdatedParamInfo, SortOrder.Ascending) },
            };

            _schemaInformation.Current = schemaVersion;
            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.True(sortingValid);
            Assert.Empty(errorMessage);
        }

        [Theory]
        [InlineData(SortOrder.Ascending, SortOrder.Ascending, true)]
        [InlineData(SortOrder.Ascending, SortOrder.Descending, false)]
        [InlineData(SortOrder.Descending, SortOrder.Ascending, false)]
        [InlineData(SortOrder.Descending, SortOrder.Descending, true)]
        public void GivenLastUpdatedAndResourceTypeDifferentSortingOrder_WhenValidating_ThenReturnsExpectedResult(SortOrder sortOrder1, SortOrder sortOrder2, bool expectedResult)
        {
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (_resourceTypeParamInfo, sortOrder1) },
                { (_lastUpdatedParamInfo, sortOrder2) },
            };

            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.Equal(expectedResult, sortingValid);
        }

        public static IEnumerable<object[]> GetSupportedSearchParamTypes()
        {
            yield return new object[] { SearchParamType.Date };
            yield return new object[] { SearchParamType.String };
        }

        public static IEnumerable<object[]> GetUnsupportedSearchParamTypes()
        {
            yield return new object[] { SearchParamType.Number };
            yield return new object[] { SearchParamType.Quantity };
            yield return new object[] { SearchParamType.Composite };
            yield return new object[] { SearchParamType.Reference };
            yield return new object[] { SearchParamType.Uri };
            yield return new object[] { SearchParamType.Token };
            yield return new object[] { SearchParamType.Special };
        }
    }
}
