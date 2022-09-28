// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSesrverSearchParameterValidatorTests
    {
        private SearchParameterToSearchValueTypeMap _parameterToSearchValueTypeMap;
        private SqlServerSearchParameterValidator _sqlServerSearchParameterValidator;

        public SqlSesrverSearchParameterValidatorTests()
        {
            _parameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();
            _sqlServerSearchParameterValidator = new SqlServerSearchParameterValidator(_parameterToSearchValueTypeMap);
        }

        [Theory]
        [MemberData(nameof(GetValidSearchParameters))]
        public void GivenASupportedType_WhenSearchParameterisValidated_ThenReturnTrue(SearchParameterInfo searchParameter)
        {
            Assert.True(_sqlServerSearchParameterValidator.ValidateSearchParameter(searchParameter, out var _));
        }

        [Theory]
        [MemberData(nameof(GetInValidSearchParameters))]
        public void GivenAnUnSupportedType_WhenSearchParameterisValidated_ThenReturnFalse(SearchParameterInfo searchParameter)
        {
            Assert.False(_sqlServerSearchParameterValidator.ValidateSearchParameter(searchParameter, out var _));
        }

        public static IEnumerable<object[]> GetValidSearchParameters()
        {
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Date) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Token) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Number) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Quantity) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Reference) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String) };
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Uri) };

            var components = new List<SearchParameterComponentInfo>();
            var component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Token);
            components.Add(component);
            component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Quantity);
            components.Add(component);
            var compositeParam = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Composite, components: components);
            yield return new object[] { compositeParam };
        }

        public static IEnumerable<object[]> GetInValidSearchParameters()
        {
            yield return new object[] { new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Special) };

            var components = new List<SearchParameterComponentInfo>();
            var component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.String);
            components.Add(component);
            component = new SearchParameterComponentInfo();
            component.ResolvedSearchParameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Quantity);
            components.Add(component);
            var compositeParam = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Composite, components: components);
            yield return new object[] { compositeParam };
        }
    }
}
