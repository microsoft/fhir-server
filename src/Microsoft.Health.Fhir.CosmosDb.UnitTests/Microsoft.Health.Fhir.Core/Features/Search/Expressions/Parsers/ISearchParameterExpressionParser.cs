// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers
{
    public interface ISearchParameterExpressionParser
    {
        Expression Parse(
            SearchParameterInfo searchParameter,
            SearchModifier modifier,
            string value);
    }
}
