// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using static Hl7.Fhir.Model.SearchParameter;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions
{
    public interface ILegacySearchValueExpressionBuilder
    {
        Expression Build(
            SearchParam searchParam,
            SearchModifierCode? modifier,
            SearchComparator comparator,
            string value);
    }
}
