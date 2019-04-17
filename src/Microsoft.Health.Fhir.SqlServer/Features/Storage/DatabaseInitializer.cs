// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class DatabaseInitializer
    {
        private ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(ILogger<DatabaseInitializer> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        public void InitializeDatabase()
        {
            _logger.LogInformation("Attempting to initialize the database.");
        }
    }
}
