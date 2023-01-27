// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Microsoft.Health.Fhir.SqlServer.Features.Storage.SqlRetryService;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlRetryServiceDelegateOptions
    {
        public bool DefaultIsExceptionRetriableOff { get; init; }

        public IsExceptionRetriable CustomIsExceptionRetriable { get; init; }
    }
}
