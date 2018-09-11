// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Legacy;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy
{
    public class SearchParamTests : SearchParamTestsBase
    {
        protected override ISearchParamBuilderBase<SearchParam> Builder { get; } = new SearchParamBuilder();

        private class SearchParamBuilder : SearchParamBuilderBase<SearchParam>
        {
            public override SearchParam ToSearchParam()
            {
                return new SearchParam(
                    ResourceType,
                    ParamName,
                    ParamType,
                    Parser);
            }
        }
    }
}
