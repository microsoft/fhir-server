// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    /// <summary>
    /// Represents a stored procedure
    /// </summary>
    public class StoredProcedure
    {
        public StoredProcedure(string procedureName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(procedureName, nameof(procedureName));

            ProcedureName = procedureName;
        }

        public string ProcedureName { get; }

        public static implicit operator string(StoredProcedure p) => p.ToString();

        public override string ToString() => ProcedureName;
    }
}
