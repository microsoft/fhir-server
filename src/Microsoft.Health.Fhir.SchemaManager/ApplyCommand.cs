// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SchemaManager.Properties;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Manager.Model;

namespace Microsoft.Health.Fhir.SchemaManager;

[SuppressMessage("Naming", "CA1710: Identifiers should have correct suffix", Justification = "Base class is also called Command.")]
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

        AddOption(CommandOptions.ConnectionStringOption());
        AddOption(CommandOptions.ManagedIdentityClientIdOption());
        AddOption(CommandOptions.AuthenticationTypeOption());
        AddOption(CommandOptions.VersionOption());
        AddOption(CommandOptions.NextOption());
        AddOption(CommandOptions.LatestOption());
        AddOption(CommandOptions.ForceOption());

        AddValidator(commandResult => MutuallyExclusiveOptionValidator.Validate(commandResult, new List<Option> { CommandOptions.VersionOption(), CommandOptions.NextOption(), CommandOptions.LatestOption() }, Resources.MutuallyExclusiveValidation));

        Handler = CommandHandler.Create(
            (MutuallyExclusiveType type, bool force, CancellationToken token)
            => ApplyHandler(type, force, token));

        _schemaManager = schemaManager;
        _logger = logger;
    }

    private Task ApplyHandler(MutuallyExclusiveType type, bool force, CancellationToken token = default)
    {
        if (force && !EnsureForce())
        {
            return Task.CompletedTask;
        }

        return _schemaManager.ApplySchema(type, token);
    }

    private bool EnsureForce()
    {
        _logger.LogWarning("Are you sure you want to force the apply command ? Type 'yes' to confirm.");
        return string.Equals(Console.ReadLine(), "yes", StringComparison.OrdinalIgnoreCase);
    }
}
