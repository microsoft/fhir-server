// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Microsoft.Health.Fhir.SqlServer.Features.Storage.SqlRetryPolicyFactory;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// This class sets the custom delegate that is used instead of, or in addition to the SqlRetryService class internal method that determines if the
    /// thrown exception represents a retriable error.
    /// </summary>
    public class SqlRetryServiceDelegateOptions
    {
        /// <summary>
        /// If set to true then the SqlRetryService class internal method that determines if the thrown exception represents a retriable error is disabled
        /// and only CustomIsExceptionRetriable is used. If false, then both are used and if either returns true it means that the thrown exception
        /// represents a retriable error.
        /// </summary>
        public bool DefaultIsExceptionRetriableOff { get; init; }

        /// <summary>
        /// If set then this delegate provides logic that determines if the thrown exception represents a retriable error. <see cref="SqlRetryPolicyFactory.IsExceptionRetriable"/>
        /// </summary>
        public IsExceptionRetriable CustomIsExceptionRetriable { get; init; }
    }
}
