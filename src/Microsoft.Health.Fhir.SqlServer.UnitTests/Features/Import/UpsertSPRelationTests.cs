// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Import
{
    public class UpsertSPRelationTests
    {
        /// <summary>
        /// New parameters added to upsert resource stored procedure might impact bulk import operation.
        /// Please contact import feature owner to review the change if this test case fail.
        /// </summary>
        [Fact]
        public void GivenUpsertResourceStoredProcedure_WhenNewParameterAdded_ThenBulkImportShouldSupportNewParameters()
        {
            string[] bulkImportSupportedParametersForResourceUpsert = new string[]
            {
                "command",
                "baseResourceSurrogateId",
                "resourceTypeId",
                "resourceId",
                "eTag",
                "allowCreate",
                "isDeleted",
                "keepHistory",
                "requireETagOnUpdate",
                "requestMethod",
                "searchParamHash",
                "rawResource",
                "resourceWriteClaims",
                "compartmentAssignments",
                "referenceSearchParams",
                "tokenSearchParams",
                "tokenTextSearchParams",
                "stringSearchParams",
                "numberSearchParams",
                "quantitySearchParams",
                "uriSearchParams",
                "dateTimeSearchParms",
                "referenceTokenCompositeSearchParams",
                "tokenTokenCompositeSearchParams",
                "tokenDateTimeCompositeSearchParams",
                "tokenQuantityCompositeSearchParams",
                "tokenStringCompositeSearchParams",
                "tokenNumberNumberCompositeSearchParams",
                "isResourceChangeCaptureEnabled",
                "comparedVersion",
            };
            MethodInfo methodInfo = typeof(VLatest.UpsertResourceProcedure).GetMethods().Where(m => m.Name.Equals("PopulateCommand")).OrderBy(m => -m.GetParameters().Count()).First();
            string[] upsertStoredProcedureParameters = methodInfo.GetParameters().Select(p => p.Name).ToArray();
            Assert.Equal(bulkImportSupportedParametersForResourceUpsert.Length, upsertStoredProcedureParameters.Length);
            foreach (string parameterName in upsertStoredProcedureParameters)
            {
                Assert.Contains(parameterName, bulkImportSupportedParametersForResourceUpsert);
            }
        }
    }
}
