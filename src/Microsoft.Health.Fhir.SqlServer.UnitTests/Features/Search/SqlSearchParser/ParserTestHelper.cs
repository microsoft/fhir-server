// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using NSubstitute;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Helper for creating parser instances with mocked dependencies in unit tests.
    /// </summary>
    internal static class ParserTestHelper
    {
        /// <summary>
        /// Creates a SqlSearchParameterDefinitionManager without invoking its constructor.
        /// The returned instance cannot resolve actual search parameters, but is sufficient
        /// for testing BuildWhereClause methods that don't access the parameter collection.
        /// </summary>
        public static SqlSearchParameterDefinitionManager CreateMockDefinitionManager()
        {
            return (SqlSearchParameterDefinitionManager)RuntimeHelpers.GetUninitializedObject(
                typeof(SqlSearchParameterDefinitionManager));
        }

        /// <summary>
        /// Creates a mocked ISqlServerFhirModel where TryGetResourceTypeId returns true
        /// for the specified resource types with their assigned IDs.
        /// </summary>
        public static ISqlServerFhirModel CreateMockFhirModel(params (string resourceType, short id)[] mappings)
        {
            var model = Substitute.For<ISqlServerFhirModel>();

            foreach (var (resourceType, id) in mappings)
            {
                model.TryGetResourceTypeId(resourceType, out Arg.Any<short>())
                    .Returns(x =>
                    {
                        x[1] = id;
                        return true;
                    });
            }

            return model;
        }
    }
}
