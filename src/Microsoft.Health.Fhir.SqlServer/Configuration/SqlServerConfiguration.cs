// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Health.Fhir.SqlServer.Configuration
{
    public class SqlServerConfiguration : ISqlServerConfiguration
    {
        private readonly IConfiguration _configuration;
        private const int DefaultCommandTimeout = 30;

        public SqlServerConfiguration(IConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            _configuration = configuration;
        }

        public int GetCommandTimeout()
        {
            if (int.TryParse(_configuration["SqlServer:SqlCommand:CommandTimeout"], out int commandTimeout) && commandTimeout >= 0)
            {
                return commandTimeout;
            }

            return DefaultCommandTimeout;
        }
    }
}
