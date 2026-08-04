// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Tenancy
{
    /// <summary>
    /// The default provider, which keeps every tenant on the process-wide SQL connection string.
    /// </summary>
    public sealed class RootTenantConnectionStringProvider : ITenantConnectionStringProvider
    {
        private readonly SqlServerDataStoreConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="RootTenantConnectionStringProvider"/> class.
        /// </summary>
        /// <param name="configuration">The root SQL configuration.</param>
        public RootTenantConnectionStringProvider(IOptions<SqlServerDataStoreConfiguration> configuration)
        {
            EnsureArg.IsNotNull(configuration?.Value, nameof(configuration));

            _configuration = configuration.Value;
        }

        /// <inheritdoc />
        public string GetConnectionString(TenantDescriptor tenant) => _configuration.ConnectionString;
    }
}
