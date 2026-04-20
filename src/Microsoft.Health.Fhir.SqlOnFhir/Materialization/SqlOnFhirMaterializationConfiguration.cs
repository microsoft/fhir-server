// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Configuration for SQL on FHIR ViewDefinition materialization.
/// Configures storage destinations for Parquet and Fabric (Delta Lake) output targets.
/// Follows the same authentication pattern as the FHIR server's $export configuration.
/// <para>
/// <b>Fabric / Delta Lake example:</b>
/// <code>
/// "SqlOnFhirMaterialization": {
///   "DefaultTarget": "Fabric",
///   "StorageAccountUri": "abfss://workspace@onelake.dfs.fabric.microsoft.com/lakehouse/Tables"
/// }
/// </code>
/// Delta tables are created at <c>{StorageAccountUri}/{ViewDefinitionName}/</c>.
/// Authentication uses <c>DefaultAzureCredential</c> when <c>StorageAccountUri</c> is set,
/// or a connection string when <c>StorageAccountConnection</c> is set.
/// </para>
/// </summary>
public class SqlOnFhirMaterializationConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "SqlOnFhirMaterialization";

    /// <summary>
    /// Gets or sets the connection string for Azure Blob Storage or ADLS.
    /// Used for connection string-based authentication (Parquet and Delta Lake targets).
    /// Mutually exclusive with <see cref="StorageAccountUri"/> (if both set, URI takes precedence).
    /// </summary>
    public string? StorageAccountConnection { get; set; }

    /// <summary>
    /// Gets or sets the URI for Azure Blob Storage, ADLS, or OneLake (for Managed Identity authentication).
    /// <para>
    /// Examples:
    /// <list type="bullet">
    ///   <item><c>https://myaccount.blob.core.windows.net</c> — Azure Blob Storage (Parquet target)</item>
    ///   <item><c>abfss://workspace@onelake.dfs.fabric.microsoft.com/lakehouse/Tables</c> — Fabric / OneLake (Delta Lake target)</item>
    /// </list>
    /// When set, authentication uses <c>DefaultAzureCredential</c> to obtain a bearer token.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Config binding requires string type for deserialization from appsettings.json")]
    public string? StorageAccountUri { get; set; }

    /// <summary>
    /// Gets or sets the default blob container name for Parquet output.
    /// Defaults to <c>sqlfhir</c> if not specified.
    /// Not used for Fabric/Delta Lake target when <see cref="StorageAccountUri"/> includes the full OneLake path.
    /// </summary>
    public string DefaultContainer { get; set; } = "sqlfhir";

    /// <summary>
    /// Gets or sets the schema segment to insert between the storage root and the table name
    /// for the Delta Lake / Fabric target. This is required for <b>schema-enabled</b> Fabric
    /// lakehouses, which expect paths of the form <c>Tables/{schema}/{tableName}/</c> — without
    /// it, Fabric treats the ViewDefinition name as the schema and reports the table contents
    /// as <i>Unidentified</i>.
    /// <para>
    /// Defaults to <c>dbo</c>. Set to <c>null</c> or empty string for schema-less lakehouses
    /// (path <c>Tables/{tableName}/</c>).
    /// </para>
    /// </summary>
    public string? DeltaSchema { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets the default materialization target when not specified per-ViewDefinition.
    /// Defaults to <see cref="MaterializationTarget.SqlServer"/>.
    /// Set to <see cref="MaterializationTarget.Fabric"/> for Delta Lake on OneLake.
    /// </summary>
    public MaterializationTarget DefaultTarget { get; set; } = MaterializationTarget.SqlServer;

    /// <summary>
    /// Gets a value indicating whether Parquet/Fabric storage is configured.
    /// </summary>
    public bool IsStorageConfigured =>
        !string.IsNullOrWhiteSpace(StorageAccountConnection) || !string.IsNullOrWhiteSpace(StorageAccountUri);
}
