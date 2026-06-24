// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlAuthenticationProviderTests
    {
        [Fact]
        public void GivenActiveDirectoryMsiAuthentication_WhenSqlClientLoadsProviders_ThenAzureProviderIsRegistered()
        {
            Assert.NotNull(SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryMSI));
        }
    }
}
