// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.SqlServer.Features.Schema
{
    public interface IScriptProvider
    {
        string GetMigrationScript(int version, bool applyFullSchemaSnapshot);

        byte[] GetMigrationScriptAsBytes(int version);
    }
}
