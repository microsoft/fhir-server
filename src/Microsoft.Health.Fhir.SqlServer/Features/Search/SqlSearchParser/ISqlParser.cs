// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public interface ISqlParser
    {
        void Parse(string name, string value, ParserOptions options);
    }
}
