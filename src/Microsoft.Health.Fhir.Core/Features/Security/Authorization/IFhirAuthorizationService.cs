// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    /// <summary>
    /// Used to determine whether the current principal is allowed to perform one or more data actions.`
    /// </summary>
    public interface IFhirAuthorizationService
    {
        /// <summary>
        /// Determines whether a set of data actions is permitted.
        /// <see cref="DataActions"/> is a flags enum. Callers can check for permissions
        /// to more than one action by ORing values together.
        /// </summary>
        /// <param name="dataActions">The set of data actions to check</param>
        /// <returns>
        /// Either:
        /// (a) the same value as the input (return == input), which means that all actions are permitted
        /// (b) a subset of the input ((return & input) != 0), which means that only some of the requested actions are permitted
        /// (c) None, (0), meaning none of the requested actions are permitted.
        /// In all cases, no bits will set on the return value that were not set on input.
        /// </returns>
        ValueTask<DataActions> CheckAccess(DataActions dataActions);
    }
}
