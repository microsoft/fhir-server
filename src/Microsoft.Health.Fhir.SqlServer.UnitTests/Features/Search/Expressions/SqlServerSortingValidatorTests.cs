// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Schema;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    public class SqlServerSortingValidatorTests
    {
        private SqlServerSortingValidator _sqlServerSortingValidator;
        private SchemaInformation _schemaInformation;

        public SqlServerSortingValidatorTests()
        {
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Max, SchemaVersionConstants.Max);
            _sqlServerSortingValidator = new SqlServerSortingValidator(_schemaInformation);
        }

        [Theory]
        [MemberData(nameof(GetSupportedSearchParamTypes))]
        public void GivenSupportedSortParameters_WhenValidating_ThenReturnsTrue(SearchParamType searchParamType)
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
        [MemberData(nameof(GetUnsupportedSearchParamTypes))]
        public void GivenUnsupportedSortParameters_WhenValidating_ThenReturnsFalse(SearchParamType searchParamType)
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
            SearchParameterInfo dateParamInfo = new SearchParameterInfo(name: "birthdate", code: "birthdate", SearchParamType.Date);
            SearchParameterInfo stringParamInfo = new SearchParameterInfo(name: "name", code: "name", SearchParamType.String);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> searchList = new List<(SearchParameterInfo, SortOrder)>()
            {
                { (dateParamInfo, SortOrder.Ascending) },
                { (stringParamInfo, SortOrder.Descending) },
            };

            bool sortingValid = _sqlServerSortingValidator.ValidateSorting(searchList, out IReadOnlyList<string> errorMessage);
            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
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
