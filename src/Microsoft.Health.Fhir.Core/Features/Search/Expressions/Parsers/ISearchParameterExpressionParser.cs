// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers
{
    public interface ISearchParameterExpressionParser
    {
        Expression Parse(
            SearchParameterInfo searchParameter,
            SearchModifierCode? modifier,
            string value);
    }
}
