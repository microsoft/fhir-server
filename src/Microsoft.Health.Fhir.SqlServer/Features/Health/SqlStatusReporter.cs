// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;
using Microsoft.Health.Fhir.Core.Data;

namespace Microsoft.Health.Fhir.SqlServer.Features.Health
{
    /// <summary>
    /// SQL implementation of <see cref="IDatabaseStatusReporter"/> using a ValueCache of CustomerKeyHealth.
    /// </summary>
    public class SqlStatusReporter : BaseDatabaseStatusReporter
    {
        private readonly ValueCache<CustomerKeyHealth> _customerKeyHealthCache;

        public SqlStatusReporter(ValueCache<CustomerKeyHealth> customerKeyHealthCache)
            : base()
        {
            _customerKeyHealthCache = EnsureArg.IsNotNull(customerKeyHealthCache, nameof(customerKeyHealthCache));
        }

        public override bool IsCustomerManagedKeyException(Exception exception)
        {
            return exception.IsCustomerManagedKeyException();
        }

        public override async Task<bool> IsCustomerManagedKeyProperlySetAsync(CancellationToken cancellationToken = default)
        {
            // Check Customer-Managed Key Health - CMK
            CustomerKeyHealth customerKeyHealth = await IsCustomerManagedKeyHealthyAsync(cancellationToken);

            // If no specific issues, return true
            return customerKeyHealth.IsHealthy;
        }

        private async Task<CustomerKeyHealth> IsCustomerManagedKeyHealthyAsync(CancellationToken cancellationToken = default)
        {
            return await _customerKeyHealthCache.GetAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
