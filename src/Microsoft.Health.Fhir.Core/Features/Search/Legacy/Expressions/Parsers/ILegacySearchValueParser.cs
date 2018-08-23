// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions
{
    public interface ILegacySearchValueParser
    {
        Expression Parse(SearchParam searchParam, string modifierOrResourceType, string value);
    }
}
