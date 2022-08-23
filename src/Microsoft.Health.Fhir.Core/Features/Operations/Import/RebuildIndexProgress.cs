// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public enum RebuildIndexProgress
    {
        Initialized,
        DisableIndexCompleted,
        InitialIndexProperties,
        SwitchOutAllTables,
        GetCommmandsForRebuildIndex,
        RunCommandForRebuildIndexCompleted,
        SwitchInAllTables,
    }
}
