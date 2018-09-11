// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy
{
    public class LegacyCompositeSearchParamTests : SearchParamTestsBase
    {
        private const SearchParamType DefaultUnderlyingSearchParamType = SearchParamType.Date;

        private CompositeSearchParamBuilder _builder = new CompositeSearchParamBuilder();

        protected override ISearchParamBuilderBase<SearchParam> Builder => _builder;

        [Fact]
        public void GivenAnUnderlyingSearchParamType_WhenInitialized_ThenCorrectTypeUnderlyingSearchParamTypeShouldBeAssigned()
        {
            CompositeSearchParam searchParam = _builder.ToSearchParam();

            Assert.Equal(DefaultUnderlyingSearchParamType, searchParam.UnderlyingSearchParamType);
        }

        private class CompositeSearchParamBuilder : SearchParamBuilderBase<CompositeSearchParam>
        {
            public CompositeSearchParamBuilder()
                : base()
            {
                UnderlyingSearchParamType = DefaultUnderlyingSearchParamType;
            }

            public SearchParamType UnderlyingSearchParamType { get; set; }

            public override CompositeSearchParam ToSearchParam()
            {
                return new CompositeSearchParam(
                    ResourceType,
                    ParamName,
                    UnderlyingSearchParamType,
                    Parser);
            }
        }
    }
}
