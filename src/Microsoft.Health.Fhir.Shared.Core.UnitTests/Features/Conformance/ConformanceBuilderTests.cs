// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    /// <summary>
    /// shared conformance tests
    /// </summary>
    public partial class ConformanceBuilderTests
    {
        private readonly ICapabilityStatementBuilder _builder;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public ConformanceBuilderTests()
        {
            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _builder = CapabilityStatementBuilder.Create(
                ModelInfoProvider.Instance,
                _searchParameterDefinitionManager);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenExecutingScalar_ThenCorrectInformationIsReturned()
        {
            string httpMicrosoftCom = "http://microsoft.com";

            _builder.Update(x => x.Url = new Uri(httpMicrosoftCom));

            ITypedElement statement = _builder.Build();

            object url = statement.Scalar("Resource.url");

            Assert.Equal(httpMicrosoftCom, url);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingAnUnknownResource_ThenAnArgumentExceptionIsThrown()
        {
            Assert.Throws<ArgumentException>(() => _builder.AddRestInteraction("foo", TypeRestfulInteraction.Create));
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingDefaultSearchParameters_ThenDocumentationIsAdded()
        {
            string description = "Logical id of this artifact";

            _searchParameterDefinitionManager.GetSearchParameters("Account")
                .Returns(new[] { new SearchParameterInfo("_id", "_id", SearchParamType.Token, description: description),  });

            _builder.AddDefaultSearchParameters();

            ITypedElement statement = _builder.Build();

            object idDocumentation = statement.Scalar($"{ResourceQuery("Account")}.searchParam.where(name = '_id').documentation");

            Assert.Equal(description, idDocumentation);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingDefaultInteractions_ThenAuditEventDoesntHaveUpdateDelete()
        {
            _builder.AddDefaultResourceInteractions();

            ITypedElement statement = _builder.Build();

            bool hasCreate = (bool)statement.Scalar($"{ResourceQuery("AuditEvent")}.interaction.where(code = '{TypeRestfulInteraction.Create}').exists()");
            bool noUpdate = (bool)statement.Scalar($"{ResourceQuery("AuditEvent")}.interaction.where(code = '{TypeRestfulInteraction.Update}').exists()");
            bool noDelete = (bool)statement.Scalar($"{ResourceQuery("AuditEvent")}.interaction.where(code = '{TypeRestfulInteraction.Delete}').exists()");

            Assert.True(hasCreate);
            Assert.False(noUpdate);
            Assert.False(noDelete);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingDefaultInteractions_ThenParameterTypeIsNotAdded()
        {
            _builder.AddDefaultResourceInteractions();

            ITypedElement statement = _builder.Build();

            bool noParameters = (bool)statement.Scalar($"{ResourceQuery(KnownResourceTypes.Parameters)}.exists()");

            Assert.False(noParameters);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingRestSearchParam_ThenTypeSearchParamIsAdded()
        {
            _builder.AddDefaultRestSearchParams();

            ITypedElement statement = _builder.Build();

            object typeDefinition = statement.Scalar($"CapabilityStatement.rest.searchParam.where(name = '_type').definition");

            Assert.Equal("http://hl7.org/fhir/SearchParameter/type", typeDefinition.ToString());
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingResourceSearchParam_ThenTypeSearchParamIsNotAddedUnderResource()
        {
            _searchParameterDefinitionManager.GetSearchParameters("Account")
               .Returns(new[] { new SearchParameterInfo("_type", "_type", SearchParamType.Token, description: "description"), });

            _builder.AddDefaultSearchParameters();

            ITypedElement statement = _builder.Build();

            object typeName = statement.Scalar($"{ResourceQuery("Account")}.searchParam.where(name = '_type').name");

            Assert.Null(typeName);
        }

        private static string ResourceQuery(string resource)
        {
            return $"CapabilityStatement.rest.resource.where(type = '{resource}')";
        }
    }
}
