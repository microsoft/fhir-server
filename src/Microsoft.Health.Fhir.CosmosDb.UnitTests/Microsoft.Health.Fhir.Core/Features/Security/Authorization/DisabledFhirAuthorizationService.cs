// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Security.Authorization;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    /// <summary>
    /// A <see cref="IAuthorizationService{TDataActions}"/> where all actions are always permitted.
    /// </summary>
    internal class DisabledFhirAuthorizationService : IAuthorizationService<DataActions>
    {
        public static readonly DisabledFhirAuthorizationService Instance = new DisabledFhirAuthorizationService();

        public ValueTask<DataActions> CheckAccess(DataActions dataActions, CancellationToken cancellationToken)
        {
            return new ValueTask<DataActions>(dataActions);
        }
    }
}
