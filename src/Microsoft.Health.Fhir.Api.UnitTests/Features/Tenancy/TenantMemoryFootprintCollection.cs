// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Excludes the process-wide managed-memory measurement from parallel xUnit execution.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class TenantMemoryFootprintCollection
    {
        /// <summary>
        /// The collection name shared by the managed-memory measurement tests.
        /// </summary>
        public const string Name = "TenantMemoryFootprint";
    }
}
