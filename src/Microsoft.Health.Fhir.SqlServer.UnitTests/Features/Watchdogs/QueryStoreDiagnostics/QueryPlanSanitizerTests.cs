// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Xml.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryPlanSanitizerTests
    {
        [Fact]
        public void GivenSingleStatementPlanWithPhiShapedParameterValues_WhenSanitized_ThenRemovesParametersAndPreservesDiagnosticContent()
        {
            // Arrange
            const string patientName = "Mikael W";
            const string medicalRecordNumber = "MRN-12345";
            string queryPlan = $@"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"" Version=""1.539"" Build=""15.0.2000.5"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText=""SELECT * FROM dbo.Patient WHERE Status = 1"" StatementType=""SELECT"">
          <QueryPlan>
            <MissingIndexes>
              <MissingIndexGroup Impact=""87.25""><MissingIndex Database=""[Fhir]"" Schema=""[dbo]"" Table=""[Patient]"" /></MissingIndexGroup>
            </MissingIndexes>
            <Warnings NoJoinPredicate=""1"" />
            <RelOp NodeId=""0"" PhysicalOp=""Clustered Index Scan"">
              <ScalarOperator ScalarString=""(123)""><Const ConstValue=""(123)"" /><Identifier><ColumnReference Column=""@patientName"" ParameterCompiledValue=""N'{patientName}'"" ParameterRuntimeValue=""N'{medicalRecordNumber}'"" /></Identifier></ScalarOperator>
            </RelOp>
            <ParameterList>
              <ColumnReference Column=""@patientName"" ParameterCompiledValue=""N'{patientName}'"" ParameterRuntimeValue=""N'{medicalRecordNumber}'"" />
            </ParameterList>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, result.Status);
            Assert.NotNull(result.Xml);
            Assert.False(result.Truncated);
            Assert.Equal(queryPlan.Length, result.OriginalLength);
            AssertNoParameterMetadata(result.Xml);
            Assert.DoesNotContain(patientName, result.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain(medicalRecordNumber, result.Xml, StringComparison.Ordinal);
            Assert.Contains("SELECT * FROM dbo.Patient WHERE Status = 1", result.Xml, StringComparison.Ordinal);
            Assert.Contains("ConstValue=\"(123)\"", result.Xml, StringComparison.Ordinal);
            Assert.Contains("MissingIndexGroup", result.Xml, StringComparison.Ordinal);
            Assert.Contains("Warnings", result.Xml, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenMultiStatementPlanWithMultipleParameterLists_WhenSanitized_ThenRemovesEveryParameterListAndValueAttribute()
        {
            // Arrange
            const string compiledValue = "Alice Smith";
            const string runtimeValue = "MRN-67890";
            string queryPlan = $@"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementText=""SELECT 1"">
          <QueryPlan>
            <ParameterList><ColumnReference Column=""@first"" ParameterCompiledValue=""N'{compiledValue}'"" /></ParameterList>
          </QueryPlan>
        </StmtSimple>
        <StmtSimple StatementText=""SELECT 2"">
          <QueryPlan>
            <ParameterList xmlns=""urn:future-showplan""><ColumnReference Column=""@second"" ParameterRuntimeValue=""N'{runtimeValue}'"" /></ParameterList>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, result.Status);
            Assert.NotNull(result.Xml);
            AssertNoParameterMetadata(result.Xml);
            Assert.DoesNotContain(compiledValue, result.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain(runtimeValue, result.Xml, StringComparison.Ordinal);
            Assert.Contains("StatementText=\"SELECT 1\"", result.Xml, StringComparison.Ordinal);
            Assert.Contains("StatementText=\"SELECT 2\"", result.Xml, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenPlansWithUnknownAndNoShowplanNamespaces_WhenSanitized_ThenRemovesParameterMetadataByLocalName()
        {
            // Arrange
            const string unknownNamespacePlan = @"<future:ShowPlanXML xmlns:future=""urn:contoso:future-showplan""><future:StmtSimple StatementText=""SELECT 3""><future:ParameterList><future:ColumnReference ParameterCompiledValue=""N'Jane Doe'"" ParameterRuntimeValue=""N'MRN-98765'"" /></future:ParameterList></future:StmtSimple></future:ShowPlanXML>";
            const string noNamespacePlan = @"<ShowPlanXML><StmtSimple StatementText=""SELECT 4""><ParameterList><ColumnReference ParameterCompiledValue=""N'John Doe'"" ParameterRuntimeValue=""N'MRN-54321'"" /></ParameterList></StmtSimple></ShowPlanXML>";

            // Act
            QueryPlanSanitizationResult unknownNamespaceResult = QueryPlanSanitizer.Sanitize(unknownNamespacePlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);
            QueryPlanSanitizationResult noNamespaceResult = QueryPlanSanitizer.Sanitize(noNamespacePlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, unknownNamespaceResult.Status);
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, noNamespaceResult.Status);
            Assert.NotNull(unknownNamespaceResult.Xml);
            Assert.NotNull(noNamespaceResult.Xml);
            AssertNoParameterMetadata(unknownNamespaceResult.Xml);
            AssertNoParameterMetadata(noNamespaceResult.Xml);
            Assert.Contains("SELECT 3", unknownNamespaceResult.Xml, StringComparison.Ordinal);
            Assert.Contains("SELECT 4", noNamespaceResult.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain("Jane Doe", unknownNamespaceResult.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain("MRN-98765", unknownNamespaceResult.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain("John Doe", noNamespaceResult.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain("MRN-54321", noNamespaceResult.Xml, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenPlanWithoutParameters_WhenSanitized_ThenPreservesThePlanInSubstance()
        {
            // Arrange
            const string queryPlan = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan""><BatchSequence><Batch><Statements><StmtSimple StatementText=""SELECT 42"" StatementType=""SELECT""><QueryPlan><RelOp NodeId=""0"" PhysicalOp=""Constant Scan""><ScalarOperator ScalarString=""(42)""><Const ConstValue=""(42)"" /></ScalarOperator></RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, result.Status);
            Assert.NotNull(result.Xml);
            Assert.False(result.Truncated);
            Assert.Equal(result.Xml.Length, result.SanitizedLength);
            Assert.True(XNode.DeepEquals(XDocument.Parse(queryPlan), XDocument.Parse(result.Xml)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void GivenUnavailablePlanXml_WhenSanitized_ThenReturnsPlanXmlUnavailable(string queryPlan)
        {
            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.PlanXmlUnavailableStatus, result.Status);
            Assert.Null(result.Xml);
            Assert.False(result.Truncated);
            Assert.Equal(0, result.OriginalLength);
            Assert.Equal(0, result.SanitizedLength);
        }

        [Fact]
        public void GivenMalformedPlanXml_WhenSanitized_ThenReturnsInvalidXmlWithoutThrowing()
        {
            // Arrange
            const string queryPlan = "<ShowPlanXML><ParameterList>";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.InvalidXmlStatus, result.Status);
            Assert.Null(result.Xml);
            Assert.False(result.Truncated);
            Assert.Equal(queryPlan.Length, result.OriginalLength);
            Assert.Equal(0, result.SanitizedLength);
        }

        [Fact]
        public void GivenPlanXmlWithADtdEntityDeclaration_WhenSanitized_ThenTheDtdIsRefusedAndNoXmlIsReturned()
        {
            // Arrange
            // Parsing is configured with DtdProcessing.Prohibit and no resolver, so a Showplan carrying a DTD is
            // rejected outright rather than having its entities expanded. The assertion that matters as much as the
            // status is that nothing comes back with it: this input never reaches removal or verification, so a
            // payload here would be an unsanitized payload.
            const string queryPlan = @"<?xml version=""1.0""?><!DOCTYPE ShowPlanXML [<!ENTITY phi ""N'Mikael W'"">]><ShowPlanXML><StmtSimple StatementText=""SELECT &phi;"" /></ShowPlanXML>";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.InvalidXmlStatus, result.Status);
            Assert.Null(result.Xml);
            Assert.False(result.Truncated);
            Assert.Equal(queryPlan.Length, result.OriginalLength);
            Assert.Equal(0, result.SanitizedLength);
        }

        [Fact]
        public void GivenSanitizedPlanExceedingFieldCap_WhenSanitized_ThenReturnsVerifiedTruncatedXml()
        {
            // Arrange
            const int fieldCap = 512;
            string queryPlan = $@"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan""><BatchSequence><Batch><Statements><StmtSimple StatementText=""{new string('x', fieldCap * 2)}"" /></Statements></Batch></BatchSequence></ShowPlanXML>";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, fieldCap);

            // Assert
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, result.Status);
            Assert.NotNull(result.Xml);
            Assert.True(result.Truncated);
            Assert.Equal(queryPlan.Length, result.OriginalLength);
            Assert.Equal(fieldCap, result.Xml.Length);
            Assert.True(result.SanitizedLength > fieldCap);
            Assert.True(result.SanitizedLength <= result.OriginalLength);
            AssertNoParameterMetadata(result.Xml);
        }

        [Fact]
        public void GivenPlanWhoseStatementTextContainsTheLiteralParameterList_WhenSanitized_ThenStillSanitizesSuccessfully()
        {
            // Arrange
            // Showplan embeds the original SQL in StatementText. A serialized-text scan would treat this plan as
            // unverifiable and drop it silently; structural verification must not.
            const string queryPlan = @"<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan""><BatchSequence><Batch><Statements><StmtSimple StatementText=""SELECT ParameterList, ParameterCompiledValue, ParameterRuntimeValue FROM dbo.ParameterList"" StatementType=""SELECT""><QueryPlan><RelOp NodeId=""0"" PhysicalOp=""Clustered Index Scan"" /></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, result.Status);
            Assert.NotNull(result.Xml);
            Assert.False(result.Truncated);
            Assert.Contains("FROM dbo.ParameterList", result.Xml, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(@"<ShowPlanXML><StmtSimple ParameterList=""N'Mikael W'"" /></ShowPlanXML>")]
        [InlineData(@"<ShowPlanXML><ParameterCompiledValue Value=""N'Mikael W'"" /></ShowPlanXML>")]
        [InlineData(@"<ShowPlanXML><StmtSimple><ParameterRuntimeValue>N'MRN-12345'</ParameterRuntimeValue></StmtSimple></ShowPlanXML>")]
        public void GivenSensitiveNameThatSurvivesRemoval_WhenSanitized_ThenFailsVerificationAndNeverReturnsXml(string queryPlan)
        {
            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizer.Sanitize(queryPlan, QueryStoreDiagnosticsWatchdog.MaxFieldLength);

            // Assert
            Assert.Equal(QueryPlanSanitizer.VerificationFailedStatus, result.Status);
            Assert.Null(result.Xml);
            Assert.False(result.Truncated);
            Assert.Equal(queryPlan.Length, result.OriginalLength);
            Assert.True(result.SanitizedLength > 0);
        }

        private static void AssertNoParameterMetadata(string queryPlan)
        {
            Assert.DoesNotContain("ParameterList", queryPlan, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ParameterCompiledValue", queryPlan, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ParameterRuntimeValue", queryPlan, StringComparison.OrdinalIgnoreCase);
        }
    }
}
