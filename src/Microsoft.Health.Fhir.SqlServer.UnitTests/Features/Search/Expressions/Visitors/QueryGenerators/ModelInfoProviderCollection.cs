// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators
{
    [CollectionDefinition(Name)]
    public class ModelInfoProviderCollection : ICollectionFixture<ModelInfoProviderFixture>
    {
        public const string Name = "ModelInfoProvider";
    }
}
