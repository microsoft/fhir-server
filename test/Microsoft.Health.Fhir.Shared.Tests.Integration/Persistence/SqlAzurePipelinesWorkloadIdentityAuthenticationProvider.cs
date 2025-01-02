// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Identity.Client;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

public class SqlAzurePipelinesWorkloadIdentityAuthenticationProvider : SqlAuthenticationProvider
{
    private readonly AzurePipelinesCredential _azurePipelinesCredential;

    public SqlAzurePipelinesWorkloadIdentityAuthenticationProvider(AzurePipelinesCredential azurePipelinesCredential)
    {
        _azurePipelinesCredential = azurePipelinesCredential;
    }

    public override async Task<SqlAuthenticationToken> AcquireTokenAsync(SqlAuthenticationParameters parameters)
    {
        var tokenContext = new TokenRequestContext(["https://database.windows.net/.default"]);
        var token = await _azurePipelinesCredential.GetTokenAsync(tokenContext, CancellationToken.None);

        return new SqlAuthenticationToken(token.Token, token.ExpiresOn);
    }

    public override bool IsSupported(SqlAuthenticationMethod authenticationMethod) => authenticationMethod.Equals(SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity);
}
