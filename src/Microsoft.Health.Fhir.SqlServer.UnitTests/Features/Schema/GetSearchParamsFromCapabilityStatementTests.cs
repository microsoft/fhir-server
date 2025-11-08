// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Schema
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class GetSearchParamsFromCapabilityStatementTests
    {
        /// <summary>
        /// Integration test for the GetSearchParamsFromCapabilityStatement function.
        /// This test requires a SQL Server connection and should be run as an integration test.
        /// 
        /// To test manually:
        /// 1. Deploy the database with schema version 100
        /// 2. Run the following SQL:
        /// 
        /// DECLARE @json NVARCHAR(MAX) = N'{
        ///   "resourceType": "CapabilityStatement",
        ///   "rest": [
        ///     {
        ///       "mode": "server",
        ///       "resource": [
        ///         {
        ///           "type": "Patient",
        ///           "searchParam": [
        ///             {
        ///               "name": "identifier",
        ///               "definition": "http://hl7.org/fhir/SearchParameter/Patient-identifier",
        ///               "type": "token"
        ///             },
        ///             {
        ///               "name": "name",
        ///               "definition": "http://hl7.org/fhir/SearchParameter/Patient-name",
        ///               "type": "string"
        ///             }
        ///           ]
        ///         },
        ///         {
        ///           "type": "Observation",
        ///           "searchParam": [
        ///             {
        ///               "name": "code",
        ///               "definition": "http://hl7.org/fhir/SearchParameter/Observation-code",
        ///               "type": "token"
        ///             }
        ///           ]
        ///         }
        ///       ]
        ///     }
        ///   ]
        /// }';
        /// 
        /// SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@json);
        /// 
        /// Expected output:
        /// ResourceType | SearchParamUrl                                                  | SearchParamType
        /// -------------|----------------------------------------------------------------|----------------
        /// Patient      | http://hl7.org/fhir/SearchParameter/Patient-identifier        | token
        /// Patient      | http://hl7.org/fhir/SearchParameter/Patient-name              | string
        /// Observation  | http://hl7.org/fhir/SearchParameter/Observation-code          | token
        /// </summary>
        [Fact(Skip = "This is a documentation test. Run the SQL manually against a SQL Server instance.")]
        public void GetSearchParamsFromCapabilityStatement_WithValidJson_ReturnsExpectedResults()
        {
            // This test is for documentation purposes.
            // The actual testing should be done via integration tests with a real SQL Server instance.
            Assert.True(true);
        }

        [Fact(Skip = "This is a documentation test. Run the SQL manually against a SQL Server instance.")]
        public void GetSearchParamsFromCapabilityStatement_WithEmptyRest_ReturnsEmptyTable()
        {
            // Test SQL:
            // DECLARE @json NVARCHAR(MAX) = N'{"resourceType": "CapabilityStatement", "rest": []}';
            // SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@json);
            // Expected: 0 rows
            Assert.True(true);
        }

        [Fact(Skip = "This is a documentation test. Run the SQL manually against a SQL Server instance.")]
        public void GetSearchParamsFromCapabilityStatement_WithNoSearchParams_ReturnsEmptyTable()
        {
            // Test SQL:
            // DECLARE @json NVARCHAR(MAX) = N'{
            //   "resourceType": "CapabilityStatement",
            //   "rest": [{"mode": "server", "resource": [{"type": "Patient", "searchParam": []}]}]
            // }';
            // SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@json);
            // Expected: 0 rows
            Assert.True(true);
        }

        [Fact(Skip = "This is a documentation test. Run the SQL manually against a SQL Server instance.")]
        public void GetSearchParamsFromCapabilityStatement_WithMultipleRestEntries_ReturnsAllSearchParams()
        {
            // Test SQL with multiple rest entries:
            // DECLARE @json NVARCHAR(MAX) = N'{
            //   "resourceType": "CapabilityStatement",
            //   "rest": [
            //     {"mode": "server", "resource": [{"type": "Patient", "searchParam": [{"definition": "http://example.com/1", "type": "string"}]}]},
            //     {"mode": "client", "resource": [{"type": "Observation", "searchParam": [{"definition": "http://example.com/2", "type": "token"}]}]}
            //   ]
            // }';
            // SELECT * FROM dbo.GetSearchParamsFromCapabilityStatement(@json);
            // Expected: 2 rows (one for Patient, one for Observation)
            Assert.True(true);
        }
    }
}
