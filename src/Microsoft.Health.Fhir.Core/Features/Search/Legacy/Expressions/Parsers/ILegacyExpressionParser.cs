// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions
{
    public interface ILegacyExpressionParser
    {
        Expression Parse(ResourceTypeManifest resourceTypeManifest, string key, string value);
    }
}
