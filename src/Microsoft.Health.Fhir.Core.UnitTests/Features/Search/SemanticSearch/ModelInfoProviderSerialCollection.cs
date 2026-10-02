// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    /// <summary>
    /// Serializes tests that replace the global ModelInfoProvider so they do not run in parallel with
    /// tests that read it and would otherwise observe a partially configured provider.
    /// </summary>
    [CollectionDefinition(nameof(ModelInfoProviderSerialCollection), DisableParallelization = true)]
    public sealed class ModelInfoProviderSerialCollection
    {
    }
}
