// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NotReferencingSqlGenerationTests
    {
        [Fact]
        public void GivenNotReferencingExpression_WhenVisited_ThenNotExistsSqlIsGenerated()
        {
            var model = Substitute.For<ISqlServerFhirModel>();
            model.GetResourceTypeId("Device").Returns((short)99);
            var patientParam = new SearchParameterInfo(
                "patient",
                "patient",
                ValueSets.SearchParamType.Reference,
                new System.Uri("http://hl7.org/fhir/SearchParameter/Device-patient"));
            model.GetSearchParamId(patientParam.Url).Returns((short)123);

            var stringBuilder = new IndentedStringBuilder(new StringBuilder());
            using var sqlCommand = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(sqlCommand.Parameters));
            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            var context = new SearchParameterQueryGeneratorContext(stringBuilder, parameters, model, schemaInformation, isAsyncOperation: false, tableAlias: null);

            var expression = new NotReferencingExpression("Device", patientParam);
            expression.AcceptVisitor(NotReferencedQueryGenerator.Instance, context);

            var sql = stringBuilder.ToString();
            Assert.Contains("= 99", sql); // ResourceTypeId filter
            Assert.Contains("NOT EXISTS", sql); // anti-join
            Assert.Contains("SearchParamId = 123", sql);
            Assert.Contains("RefResourceSurrogateId = ResourceSurrogateId", sql);
        }
    }
}
