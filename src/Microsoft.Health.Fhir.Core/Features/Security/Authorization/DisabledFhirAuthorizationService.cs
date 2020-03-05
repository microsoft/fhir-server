// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    /// <summary>
    /// A <see cref="IFhirAuthorizationService"/> where all actions are always permitted.
    /// </summary>
    internal class DisabledFhirAuthorizationService : IFhirAuthorizationService
    {
        public static readonly DisabledFhirAuthorizationService Instance = new DisabledFhirAuthorizationService();

        public ValueTask<DataActions> CheckAccess(DataActions dataActions)
        {
            return new ValueTask<DataActions>(dataActions);
        }
    }
}
