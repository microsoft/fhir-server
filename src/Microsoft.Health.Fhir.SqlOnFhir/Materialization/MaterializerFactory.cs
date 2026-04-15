// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir.Materialization;

/// <summary>
/// Resolves the correct <see cref="IViewDefinitionMaterializer"/> implementation(s) based on
/// the <see cref="MaterializationTarget"/>. When multiple targets are specified (flags),
/// delegates to all applicable materializers.
/// </summary>
public sealed class MaterializerFactory
{
    private readonly IViewDefinitionMaterializer _sqlMaterializer;
    private readonly IViewDefinitionMaterializer? _parquetMaterializer;
    private readonly IViewDefinitionMaterializer? _deltaLakeMaterializer;
    private readonly SqlOnFhirMaterializationConfiguration _config;
    private readonly ILogger<MaterializerFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterializerFactory"/> class.
    /// </summary>
    /// <param name="sqlMaterializer">The SQL Server materializer (always available).</param>
    /// <param name="config">The materialization configuration.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="parquetMaterializer">The Parquet materializer (null if storage not configured).</param>
    /// <param name="deltaLakeMaterializer">The Delta Lake materializer for Fabric target (null if storage not configured).</param>
    public MaterializerFactory(
        IViewDefinitionMaterializer sqlMaterializer,
        IOptions<SqlOnFhirMaterializationConfiguration> config,
        ILogger<MaterializerFactory> logger,
        IViewDefinitionMaterializer? parquetMaterializer = null,
        IViewDefinitionMaterializer? deltaLakeMaterializer = null)
    {
        _sqlMaterializer = sqlMaterializer;
        _parquetMaterializer = parquetMaterializer;
        _deltaLakeMaterializer = deltaLakeMaterializer;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets the default materialization target from configuration.
    /// </summary>
    public MaterializationTarget DefaultTarget => _config.DefaultTarget;

    /// <summary>
    /// Validates whether the specified materialization target can be satisfied with the current
    /// server configuration. Returns <c>null</c> if valid, or an error message describing
    /// why the target cannot be achieved.
    /// </summary>
    /// <param name="target">The materialization target(s) to validate.</param>
    /// <returns><c>null</c> if the target is achievable; otherwise, a human-readable error message.</returns>
    public string? ValidateTarget(MaterializationTarget target)
    {
        if (target == MaterializationTarget.None)
        {
            return "No materialization target specified.";
        }

        if (target.HasFlag(MaterializationTarget.Parquet) && _parquetMaterializer == null)
        {
            return "Parquet materialization requested but storage is not configured. " +
                   "Set SqlOnFhirMaterialization:StorageAccountUri or StorageAccountConnection in appsettings.json.";
        }

        if (target.HasFlag(MaterializationTarget.Fabric) && _deltaLakeMaterializer == null)
        {
            return "Fabric (Delta Lake) materialization requested but storage is not configured. " +
                   "Set SqlOnFhirMaterialization:StorageAccountUri to a OneLake abfss:// URI in appsettings.json " +
                   "(e.g., abfss://workspace@onelake.dfs.fabric.microsoft.com/lakehouse/Tables).";
        }

        return null;
    }

    /// <summary>
    /// Gets all materializers for the specified target.
    /// The caller must call <see cref="ValidateTarget"/> before invoking this method to ensure
    /// the target can be satisfied. If no materializers are resolved, an empty list is returned
    /// and an error is logged — the system will <b>not</b> silently fall back to SQL Server.
    /// </summary>
    /// <param name="target">The materialization target(s).</param>
    /// <returns>A list of materializers that should be invoked.</returns>
    public IReadOnlyList<IViewDefinitionMaterializer> GetMaterializers(MaterializationTarget target)
    {
        var materializers = new List<IViewDefinitionMaterializer>();

        if (target.HasFlag(MaterializationTarget.SqlServer))
        {
            materializers.Add(_sqlMaterializer);
        }

        if (target.HasFlag(MaterializationTarget.Parquet))
        {
            if (_parquetMaterializer != null)
            {
                materializers.Add(_parquetMaterializer);
            }
            else
            {
                _logger.LogError(
                    "Parquet materialization requested but storage is not configured. " +
                    "Set SqlOnFhirMaterialization:StorageAccountUri or StorageAccountConnection in appsettings.json");
            }
        }

        if (target.HasFlag(MaterializationTarget.Fabric))
        {
            if (_deltaLakeMaterializer != null)
            {
                materializers.Add(_deltaLakeMaterializer);
            }
            else
            {
                _logger.LogError(
                    "Fabric (Delta Lake) materialization requested but storage is not configured. " +
                    "Set SqlOnFhirMaterialization:StorageAccountUri in appsettings.json");
            }
        }

        if (materializers.Count == 0)
        {
            _logger.LogError(
                "No materializers resolved for target '{Target}'. The target cannot be satisfied with current configuration",
                target);
        }

        return materializers;
    }

    /// <summary>
    /// Upserts a resource across all materializers for the given target.
    /// </summary>
    public async Task<int> UpsertResourceAsync(
        MaterializationTarget target,
        string viewDefinitionJson,
        string viewDefinitionName,
        ResourceElement resource,
        string resourceKey,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IViewDefinitionMaterializer> materializers = GetMaterializers(target);
        if (materializers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot materialize ViewDefinition '{viewDefinitionName}': no materializers available for target '{target}'. " +
                "Verify that storage is configured in SqlOnFhirMaterialization settings.");
        }

        int totalRows = 0;

        foreach (IViewDefinitionMaterializer materializer in materializers)
        {
            int rows = await materializer.UpsertResourceAsync(
                viewDefinitionJson, viewDefinitionName, resource, resourceKey, cancellationToken);
            totalRows = Math.Max(totalRows, rows);
        }

        return totalRows;
    }

    /// <summary>
    /// Deletes a resource across all materializers for the given target.
    /// </summary>
    public async Task<int> DeleteResourceAsync(
        MaterializationTarget target,
        string viewDefinitionName,
        string resourceKey,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IViewDefinitionMaterializer> materializers = GetMaterializers(target);
        if (materializers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete from ViewDefinition '{viewDefinitionName}': no materializers available for target '{target}'. " +
                "Verify that storage is configured in SqlOnFhirMaterialization settings.");
        }

        int totalDeleted = 0;

        foreach (IViewDefinitionMaterializer materializer in materializers)
        {
            int deleted = await materializer.DeleteResourceAsync(
                viewDefinitionName, resourceKey, cancellationToken);
            totalDeleted = Math.Max(totalDeleted, deleted);
        }

        return totalDeleted;
    }

    /// <summary>
    /// Ensures storage exists across all materializers for the given target.
    /// </summary>
    public async Task EnsureStorageAsync(
        MaterializationTarget target,
        string viewDefinitionJson,
        string viewDefinitionName,
        CancellationToken cancellationToken)
    {
        foreach (IViewDefinitionMaterializer materializer in GetMaterializers(target))
        {
            await materializer.EnsureStorageAsync(viewDefinitionJson, viewDefinitionName, cancellationToken);
        }
    }

    /// <summary>
    /// Checks whether storage exists across any materializer for the given target.
    /// </summary>
    public async Task<bool> StorageExistsAsync(
        MaterializationTarget target,
        string viewDefinitionName,
        CancellationToken cancellationToken)
    {
        foreach (IViewDefinitionMaterializer materializer in GetMaterializers(target))
        {
            if (await materializer.StorageExistsAsync(viewDefinitionName, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cleans up storage across all materializers for the given target.
    /// </summary>
    public async Task CleanupStorageAsync(
        MaterializationTarget target,
        string viewDefinitionName,
        CancellationToken cancellationToken)
    {
        foreach (IViewDefinitionMaterializer materializer in GetMaterializers(target))
        {
            await materializer.CleanupStorageAsync(viewDefinitionName, cancellationToken);
        }
    }
}
