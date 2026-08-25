// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// The outcome of settling a client ETag or an internal ComparedVersion guard against the current state of
    /// a document, for a write that has been reduced to a logical no-op and so has no write of its own for the
    /// service to evaluate that guard against.
    /// </summary>
    /// <remarks>
    /// <see cref="Unconfirmable"/> is deliberately the zero/default value. An uninitialized or defaulted
    /// <see cref="GuardConfirmationResult"/> - for example from a field or local that was never assigned - must
    /// fail closed rather than silently behave as if the guard were <see cref="Confirmed"/>. Every value is
    /// assigned explicitly so this ordering can never drift back to an unsafe default by accident.
    /// </remarks>
    internal enum GuardConfirmationResult
    {
        /// <summary>
        /// The guard cannot be settled against the current document at all, because the document offers nothing
        /// to make a conditional write out of. Retrying cannot change that, so the caller fails closed. This is
        /// the default value of the enum, so any code path that forgets to assign a result fails closed too.
        /// </summary>
        Unconfirmable = 0,

        /// <summary>
        /// The guard was settled against the current document: the version the caller asked for is still the
        /// one stored, as judged by the service rather than by a read.
        /// </summary>
        Confirmed = 1,

        /// <summary>
        /// Another write reached the document first, so the document the guard was checked against is no longer
        /// current. Re-reading and re-validating may still succeed.
        /// </summary>
        Superseded = 2,
    }
}
