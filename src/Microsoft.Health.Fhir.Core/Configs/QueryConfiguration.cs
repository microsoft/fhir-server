// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class QueryConfiguration
    {
        /// <summary>
        /// If Dynamic Sql Query Plan Selection is enabled.
        /// </summary>
        public bool DynamicSqlQueryPlanSelectionEnabled { get; set; } = false;
    }
}
