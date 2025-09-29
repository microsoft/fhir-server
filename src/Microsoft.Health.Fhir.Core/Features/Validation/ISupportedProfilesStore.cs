// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public interface ISupportedProfilesStore
    {
        /// <summary>
        /// Provide supported profiles for specified <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">Resource type to get profiles.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="disableCacheRefresh">Should we check server for new updates or get data out of cache.</param>
        Task<IEnumerable<string>> GetSupportedProfilesAsync(string resourceType, CancellationToken cancellationToken, bool disableCacheRefresh = false);

        /// <summary>
        /// Provide supported profiles for specified.
        /// </summary>
        IReadOnlySet<string> GetProfilesTypes();

        void Refresh();
    }
}
