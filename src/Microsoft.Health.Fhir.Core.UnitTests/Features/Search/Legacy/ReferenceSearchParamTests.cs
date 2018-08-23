// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy
{
    public class ReferenceSearchParamTests : SearchParamTestsBase
    {
        private const string ParamNameTargetResourceTypes = "targetResourceTypes";

        private static readonly IReadOnlyCollection<Type> DefaultTargetResourceTypes = new Type[] { typeof(Observation) };

        private ReferenceSearchParamBuilder _builder = new ReferenceSearchParamBuilder();

        protected override ISearchParamBuilderBase<SearchParam> Builder => _builder;

        [Fact]
        public void GivenANullTargetResourceTypes_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            _builder.TargetResourceTypes = null;

            Assert.Throws<ArgumentNullException>(ParamNameTargetResourceTypes, () => Builder.ToSearchParam());
        }

        [Fact]
        public void GivenATargetResourceTypes_WhenInitialized_ThenCorrectTargetResourceTypesShouldBeAssigned()
        {
            ReferenceSearchParam searchParam = _builder.ToSearchParam();

            Assert.Equal(DefaultTargetResourceTypes, searchParam.TargetReferenceTypes);
        }

        private class ReferenceSearchParamBuilder : SearchParamBuilderBase<ReferenceSearchParam>
        {
            public ReferenceSearchParamBuilder()
            {
                TargetResourceTypes = DefaultTargetResourceTypes;
            }

            public IReadOnlyCollection<Type> TargetResourceTypes { get; set; }

            public override ReferenceSearchParam ToSearchParam()
            {
                return new ReferenceSearchParam(
                    ResourceType,
                    ParamName,
                    Parser,
                    TargetResourceTypes);
            }
        }
    }
}
