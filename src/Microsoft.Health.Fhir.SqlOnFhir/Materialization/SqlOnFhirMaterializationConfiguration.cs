// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Configuration for SQL on FHIR ViewDefinition materialization.
/// Configures storage destinations for Parquet/Fabric output targets.
/// Follows the same authentication pattern as the FHIR server's $export configuration.
/// </summary>
public class SqlOnFhirMaterializationConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "SqlOnFhirMaterialization";

    /// <summary>
    /// Gets or sets the connection string for Azure Blob Storage or ADLS.
    /// Used for connection string-based authentication.
    /// Mutually exclusive with <see cref="StorageAccountUri"/> (if both set, URI takes precedence).
    /// </summary>
    public string? StorageAccountConnection { get; set; }

    /// <summary>
    /// Gets or sets the URI for Azure Blob Storage or ADLS (for Managed Identity authentication).
    /// Example: <c>https://myaccount.blob.core.windows.net</c> or <c>https://onelake.dfs.fabric.microsoft.com</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Config binding requires string type for deserialization from appsettings.json")]
    public string? StorageAccountUri { get; set; }

    /// <summary>
    /// Gets or sets the default blob container name for Parquet output.
    /// Defaults to <c>sqlfhir</c> if not specified.
    /// </summary>
    public string DefaultContainer { get; set; } = "sqlfhir";

    /// <summary>
    /// Gets or sets the default materialization target when not specified per-ViewDefinition.
    /// Defaults to <see cref="MaterializationTarget.SqlServer"/>.
    /// </summary>
    public MaterializationTarget DefaultTarget { get; set; } = MaterializationTarget.SqlServer;

    /// <summary>
    /// Gets a value indicating whether Parquet/Fabric storage is configured.
    /// </summary>
    public bool IsStorageConfigured =>
        !string.IsNullOrWhiteSpace(StorageAccountConnection) || !string.IsNullOrWhiteSpace(StorageAccountUri);
}
