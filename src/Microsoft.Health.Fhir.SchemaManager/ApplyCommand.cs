// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SchemaManager.Properties;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Manager.Model;

namespace Microsoft.Health.Fhir.SchemaManager;

[SuppressMessage("Naming", "CA1710: Identifiers should have correct suffix", Justification = "Base class is also called Command.")]
[SuppressMessage("Design", "CA1010: Generic interface should also be implemented", Justification = "System.CommandLine.Command implements only the non-generic IEnumerable interface.")]
public class ApplyCommand : Command
{
    private readonly ISchemaManager _schemaManager;
    private readonly ILogger<ApplyCommand> _logger;

    public ApplyCommand(
        ISchemaManager schemaManager,
        ILogger<ApplyCommand> logger)
        : base(CommandNames.Apply, Resources.ApplyCommandDescription)
    {
        EnsureArg.IsNotNull(logger, nameof(logger));
        EnsureArg.IsNotNull(schemaManager, nameof(schemaManager));

        var versionOption = CommandOptions.VersionOption();
        var nextOption = CommandOptions.NextOption();
        var latestOption = CommandOptions.LatestOption();
        var forceOption = CommandOptions.ForceOption();

        Options.Add(CommandOptions.ConnectionStringOption());
        Options.Add(CommandOptions.ManagedIdentityClientIdOption());
        Options.Add(CommandOptions.AuthenticationTypeOption());
        Options.Add(CommandOptions.EnableWorkloadIdentityOptions());
        Options.Add(versionOption);
        Options.Add(nextOption);
        Options.Add(latestOption);
        Options.Add(forceOption);

        Validators.Add(commandResult => MutuallyExclusiveOptionValidator.Validate(commandResult, new List<Option> { versionOption, nextOption, latestOption }, Resources.MutuallyExclusiveValidation));

        SetAction((parseResult, token) => ApplyHandler(
            new MutuallyExclusiveType
            {
                Version = parseResult.GetValue(versionOption),
                Next = parseResult.GetValue(nextOption),
                Latest = parseResult.GetValue(latestOption),
            },
            parseResult.GetValue(forceOption),
            token));

        _schemaManager = schemaManager;
        _logger = logger;
    }

    public async Task ExecuteAsync(MutuallyExclusiveType type, bool force, CancellationToken token = default)
    {
        await ApplyHandler(type, force, token);
    }

    private Task ApplyHandler(MutuallyExclusiveType type, bool force, CancellationToken token = default)
    {
        if (force)
        {
            _logger.LogInformation("Forcing the apply command");
        }

        return _schemaManager.ApplySchema(type, force, token);
    }

    private bool EnsureForce()
    {
        _logger.LogWarning("Are you sure you want to force the apply command ? Type 'yes' to confirm.");
        return string.Equals(Console.ReadLine(), "yes", StringComparison.OrdinalIgnoreCase);
    }
}
