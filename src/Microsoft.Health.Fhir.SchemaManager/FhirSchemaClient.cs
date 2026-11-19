// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Exceptions;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Manager.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SchemaManager;

public class FhirSchemaClient : ISchemaClient
{
    private readonly IScriptProvider _scriptProvider;
    private readonly ISchemaDataStore _schemaDataStore;
    private readonly ISchemaManagerDataStore _schemaManagerDataStore;
    private readonly ILogger<FhirSchemaClient> _logger;

    public FhirSchemaClient(
        IScriptProvider scriptProvider,
        ISchemaDataStore schemaDataStore,
        ISchemaManagerDataStore schemaManagerDataStore,
        ILogger<FhirSchemaClient> logger)
    {
        EnsureArg.IsNotNull(scriptProvider, nameof(scriptProvider));
        EnsureArg.IsNotNull(schemaDataStore, nameof(schemaDataStore));
        EnsureArg.IsNotNull(schemaManagerDataStore, nameof(schemaManagerDataStore));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _scriptProvider = scriptProvider;
        _schemaDataStore = schemaDataStore;
        _schemaManagerDataStore = schemaManagerDataStore;
        _logger = logger;
    }

    public async Task<List<AvailableVersion>> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        int currentVersion = await _schemaManagerDataStore.GetCurrentSchemaVersionAsync(cancellationToken);

        if (currentVersion > SchemaVersionConstants.Max)
        {
            // Expected after a failover to a region running an older build; skipping lets the schema job succeed.
            _logger.LogError("Database schema version {CurrentVersion} is ahead of the maximum version {MaxVersion} supported by this build. Skipping schema upgrade; no migrations will be applied.", currentVersion, SchemaVersionConstants.Max);
            return new List<AvailableVersion>();
        }

        int numberOfAvailableVersions = SchemaVersionConstants.Max - currentVersion + 1;
        var availableVersions = Enumerable
            .Range(currentVersion, numberOfAvailableVersions)
            .Select(version => new AvailableVersion(
                id: version,
                scriptUri: $"{version}.sql",
                diffUri: version > 1 ? $"{version}.diff.sql" : string.Empty))
            .ToList();

        return availableVersions;
    }

    public async Task<CompatibleVersion> GetCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        CompatibleVersions compatibleVersions;
        try
        {
            compatibleVersions = await _schemaDataStore.GetLatestCompatibleVersionsAsync(cancellationToken);
        }
        catch (CompatibleVersionsNotFoundException)
        {
            compatibleVersions = new CompatibleVersions(0, SchemaVersionConstants.Max);
        }

        return new CompatibleVersion(compatibleVersions.Min, compatibleVersions.Max);
    }

    public async Task<List<CurrentVersion>> GetCurrentVersionInformationAsync(CancellationToken cancellationToken = default)
    {
        List<CurrentVersionInformation>? currentVersions = await _schemaDataStore.GetCurrentVersionAsync(cancellationToken);

        IEnumerable<CurrentVersion> versions = currentVersions.Select(version => new CurrentVersion(version.Id, version.Status.ToString(), new ReadOnlyCollection<string>(version.Servers)));

        return versions.ToList();
    }

    public Task<string> GetDiffScriptAsync(int version, CancellationToken cancellationToken = default)
    {
        string diffScript = _scriptProvider.GetMigrationScript(version, false);
        return Task.FromResult(diffScript);
    }

    public Task<string> GetScriptAsync(int version, CancellationToken cancellationToken = default)
    {
        string script = _scriptProvider.GetMigrationScript(version, true);
        return Task.FromResult(script);
    }
}
