﻿// -------------------------------------------------------------------------------------------------
// <copyright file="FhirBaseSchemaRunner.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SchemaManager;

using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Manager.Exceptions;

public class FhirBaseSchemaRunner : IBaseSchemaRunner
{
    private readonly BaseSchemaRunner _baseSchemaRunner;
    private readonly ILogger<BaseSchemaRunner> _logger;

    public FhirBaseSchemaRunner(
        BaseSchemaRunner baseSchemaRunner,
        ILogger<BaseSchemaRunner> logger)
    {
        _baseSchemaRunner = EnsureArg.IsNotNull(baseSchemaRunner, nameof(baseSchemaRunner));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
    }

    public async Task EnsureBaseSchemaExistsAsync(CancellationToken cancellationToken)
    {
        await _baseSchemaRunner.EnsureBaseSchemaExistsAsync(cancellationToken);
    }

    public async Task EnsureInstanceSchemaRecordExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _baseSchemaRunner.EnsureInstanceSchemaRecordExistsAsync(cancellationToken);
        }
        catch (InstanceSchemaNotFoundException)
        {
            _logger.LogInformation("There was no current information found, this is a new DB.");
        }
    }
}
