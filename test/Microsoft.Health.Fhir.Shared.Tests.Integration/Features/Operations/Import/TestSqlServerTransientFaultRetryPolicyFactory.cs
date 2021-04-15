// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.SqlServer.Features.Client;
using Polly;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    public class TestSqlServerTransientFaultRetryPolicyFactory : ISqlServerTransientFaultRetryPolicyFactory
    {
        public IAsyncPolicy Create()
        {
            return Policy.TimeoutAsync(60);
        }
    }
}
