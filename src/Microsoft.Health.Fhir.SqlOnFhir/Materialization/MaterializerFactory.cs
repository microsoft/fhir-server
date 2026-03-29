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
    private readonly SqlOnFhirMaterializationConfiguration _config;
    private readonly ILogger<MaterializerFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterializerFactory"/> class.
    /// </summary>
    /// <param name="sqlMaterializer">The SQL Server materializer (always available).</param>
    /// <param name="config">The materialization configuration.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="parquetMaterializer">The Parquet materializer (null if storage not configured).</param>
    public MaterializerFactory(
        IViewDefinitionMaterializer sqlMaterializer,
        IOptions<SqlOnFhirMaterializationConfiguration> config,
        ILogger<MaterializerFactory> logger,
        IViewDefinitionMaterializer? parquetMaterializer = null)
    {
        _sqlMaterializer = sqlMaterializer;
        _parquetMaterializer = parquetMaterializer;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets the default materialization target from configuration.
    /// </summary>
    public MaterializationTarget DefaultTarget => _config.DefaultTarget;

    /// <summary>
    /// Gets all materializers for the specified target.
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

        if (target.HasFlag(MaterializationTarget.Parquet) || target.HasFlag(MaterializationTarget.Fabric))
        {
            if (_parquetMaterializer != null)
            {
                materializers.Add(_parquetMaterializer);
            }
            else
            {
                _logger.LogWarning(
                    "Parquet/Fabric materialization requested but storage is not configured. " +
                    "Set SqlOnFhirMaterialization:StorageAccountUri or StorageAccountConnection in appsettings.json");
            }
        }

        if (materializers.Count == 0)
        {
            _logger.LogWarning("No materializers resolved for target '{Target}', falling back to SQL Server", target);
            materializers.Add(_sqlMaterializer);
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
        int totalRows = 0;

        foreach (IViewDefinitionMaterializer materializer in GetMaterializers(target))
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
        int totalDeleted = 0;

        foreach (IViewDefinitionMaterializer materializer in GetMaterializers(target))
        {
            int deleted = await materializer.DeleteResourceAsync(
                viewDefinitionName, resourceKey, cancellationToken);
            totalDeleted = Math.Max(totalDeleted, deleted);
        }

        return totalDeleted;
    }
}
