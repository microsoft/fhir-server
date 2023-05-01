// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Polly;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public interface ISqlRetryPolicyFactory
    {
        SqlRetryBuilder CreateRetryPolicy();

        SqlRetryBuilder FromRetryPolicy(PolicyBuilder policyBuilder);
    }
}
