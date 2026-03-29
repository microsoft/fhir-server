// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Specifies where ViewDefinition results should be materialized.
/// </summary>
[Flags]
public enum MaterializationTarget
{
    /// <summary>
    /// No materialization target specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// Materialize to SQL Server tables in the <c>sqlfhir</c> schema.
    /// Best for: real-time operational analytics, CDS, quality dashboards.
    /// </summary>
    SqlServer = 1,

    /// <summary>
    /// Materialize to Parquet files in Azure Blob Storage or ADLS.
    /// Best for: research exports, ML datasets, analytics tools (Spark, DuckDB, Pandas).
    /// Also works with Microsoft Fabric when storage URI points to OneLake (ADLS Gen2).
    /// </summary>
    Parquet = 2,

    /// <summary>
    /// Reserved for future Microsoft Fabric-specific optimizations (Delta Lake, Lakehouse conventions).
    /// Currently falls back to <see cref="Parquet"/> behavior.
    /// </summary>
    Fabric = 4,
}
