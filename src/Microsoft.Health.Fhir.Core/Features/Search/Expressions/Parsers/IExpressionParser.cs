// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers
{
    public interface IExpressionParser
    {
        Expression Parse(string resourceType, string key, string value);
    }
}
