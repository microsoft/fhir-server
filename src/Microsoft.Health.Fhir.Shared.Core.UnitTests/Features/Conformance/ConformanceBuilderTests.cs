// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;
using TypeRestfulInteraction = Microsoft.Health.Fhir.ValueSets.TypeRestfulInteraction;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    /// <summary>
    /// shared conformance tests
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public partial class ConformanceBuilderTests
    {
        private readonly ICapabilityStatementBuilder _builder;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISupportedProfilesStore _supportedProfiles;
        private readonly IUrlResolver _urlResolver;

        public ConformanceBuilderTests()
        {
            IOptions<CoreFeatureConfiguration> configuration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            configuration.Value.Returns(new CoreFeatureConfiguration());

            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _supportedProfiles = Substitute.For<ISupportedProfilesStore>();
            _urlResolver = Substitute.For<IUrlResolver>();
            _urlResolver.ResolveMetadataUrl(Arg.Any<bool>()).Returns(new System.Uri("https://test.com"));

            _builder = CapabilityStatementBuilder.Create(
                ModelInfoProvider.Instance,
                _searchParameterDefinitionManager,
                configuration,
                _supportedProfiles,
                _urlResolver);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenExecutingScalar_ThenCorrectInformationIsReturned()
        {
            string httpMicrosoftCom = "http://microsoft.com";

            _builder.Apply(x => x.Url = new Uri(httpMicrosoftCom));

            ITypedElement statement = _builder.Build();

            object url = statement.Scalar("Resource.url");

            Assert.Equal(httpMicrosoftCom, url);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenApplyToUnknownResource_ThenAnArgumentExceptionIsThrown()
        {
            Assert.Throws<ArgumentException>(() => _builder.ApplyToResource("foo", c => c.ConditionalCreate = true));
        }

        [Theory]
        [InlineData("patient")]
        [InlineData("Patient")]
        [InlineData("PaTient")]
        public void GivenAConformanceBuilder_WhenVersionofResourceIsDifferentFromDefault_ThenResourceUsesResourceSpecificVersionLogic(string resourceType)
        {
            IOptions<CoreFeatureConfiguration> configuration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            Dictionary<string, string> overrides = new();
            VersioningConfiguration versionConfig = new();
            versionConfig.ResourceTypeOverrides.Add(resourceType, "no-version");

            configuration.Value.Returns(new CoreFeatureConfiguration() { Versioning = versionConfig });
            var supportedProfiles = Substitute.For<ISupportedProfilesStore>();
            var builder = CapabilityStatementBuilder.Create(
                ModelInfoProvider.Instance,
                _searchParameterDefinitionManager,
                configuration,
                supportedProfiles,
                _urlResolver);
            ICapabilityStatementBuilder capabilityStatement = builder.ApplyToResource("Patient", c =>
            {
                c.Interaction.Add(new ResourceInteractionComponent
                {
                    Code = "create",
                });
            });
            ITypedElement resource = capabilityStatement.Build();

            var patientResource = ((CapabilityStatement)resource.ToPoco()).Rest.First().Resource.First();

            Assert.True(patientResource.Type == ResourceType.Patient);
            Assert.True(patientResource.Versioning == CapabilityStatement.ResourceVersionPolicy.NoVersion);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenResourceTypeOverridesContainsResourcesThatDontMatch_ThenResourceUsesDefaultVersionLogic()
        {
            IOptions<CoreFeatureConfiguration> configuration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            Dictionary<string, string> overrides = new();
            VersioningConfiguration versionConfig = new();
            versionConfig.ResourceTypeOverrides.Add("blah", "no-version");

            configuration.Value.Returns(new CoreFeatureConfiguration() { Versioning = versionConfig });
            var supportedProfiles = Substitute.For<ISupportedProfilesStore>();
            var builder = CapabilityStatementBuilder.Create(
                ModelInfoProvider.Instance,
                _searchParameterDefinitionManager,
                configuration,
                supportedProfiles,
                _urlResolver);
            ICapabilityStatementBuilder capabilityStatement = builder.ApplyToResource("Patient", c =>
            {
                c.Interaction.Add(new ResourceInteractionComponent
                {
                    Code = "create",
                });
            });
            ITypedElement resource = capabilityStatement.Build();

            var patientResource = ((CapabilityStatement)resource.ToPoco()).Rest.First().Resource.First();

            Assert.True(patientResource.Type == ResourceType.Patient);
            Assert.True(patientResource.Versioning == CapabilityStatement.ResourceVersionPolicy.Versioned);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenResourceTypeOverridesIsEmpty_ThenResourceUsesDefaultVersionLogic()
        {
            IOptions<CoreFeatureConfiguration> configuration = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            Dictionary<string, string> overrides = new();
            VersioningConfiguration versionConfig = new();

            configuration.Value.Returns(new CoreFeatureConfiguration() { Versioning = versionConfig });
            var supportedProfiles = Substitute.For<ISupportedProfilesStore>();
            var builder = CapabilityStatementBuilder.Create(
                ModelInfoProvider.Instance,
                _searchParameterDefinitionManager,
                configuration,
                supportedProfiles,
                _urlResolver);
            ICapabilityStatementBuilder capabilityStatement = builder.ApplyToResource("Patient", c =>
            {
                c.Interaction.Add(new ResourceInteractionComponent
                {
                    Code = "create",
                });
            });
            ITypedElement resource = capabilityStatement.Build();

            var patientResource = ((CapabilityStatement)resource.ToPoco()).Rest.First().Resource.First();

            Assert.True(patientResource.Type == ResourceType.Patient);
            Assert.True(patientResource.Versioning == CapabilityStatement.ResourceVersionPolicy.Versioned);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenSyncSearchParameters_ThenDocumentationIsAdded()
        {
            string description = "Logical id of this artifact";

            _searchParameterDefinitionManager.GetSearchParameters("Account")
                .Returns(new[] { new SearchParameterInfo("_id", "_id", SearchParamType.Token, description: description), });

            _builder.SyncSearchParameters();

            ITypedElement statement = _builder.Build();

            object idDocumentation = statement.Scalar($"{ResourceQuery("Account")}.searchParam.where(name = '_id').documentation");

            Assert.Equal(description, idDocumentation);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingDefaultInteractions_ThenAuditEventDoesntHaveUpdateDelete()
        {
            _builder.PopulateDefaultResourceInteractions();

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
            _builder.PopulateDefaultResourceInteractions();

            ITypedElement statement = _builder.Build();

            bool noParameters = (bool)statement.Scalar($"{ResourceQuery(KnownResourceTypes.Parameters)}.exists()");

            Assert.False(noParameters);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingGlobalSearchParam_ThenTypeSearchParamIsAdded()
        {
            _builder.AddGlobalSearchParameters();

            ITypedElement statement = _builder.Build();

            object typeDefinition = statement.Scalar($"CapabilityStatement.rest.searchParam.where(name = '_type').definition");

            Assert.Equal("http://hl7.org/fhir/SearchParameter/type", typeDefinition.ToString());
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingResourceSearchParamAndSync_ThenTypeSearchParamIsNotAddedUnderResource()
        {
            _searchParameterDefinitionManager.GetSearchParameters("Account")
               .Returns(new[] { new SearchParameterInfo("_type", "_type", SearchParamType.Token, description: "description"), });

            _builder.SyncSearchParameters();

            ITypedElement statement = _builder.Build();

            object typeName = statement.Scalar($"{ResourceQuery("Account")}.searchParam.where(name = '_type').name");

            Assert.Null(typeName);
        }

        [Fact]
        public void GivenAConformanceBuilder_WhenAddingSupportedProfile_ThenSupportedProfilePresent()
        {
            string profile = "coolProfile";
            _supportedProfiles.GetSupportedProfiles("Account").Returns(new[] { profile });
            _builder.PopulateDefaultResourceInteractions().SyncProfiles();
            ITypedElement statement = _builder.Build();
            string fhirPath = ModelInfoProvider.Version == FhirSpecification.Stu3
                ? $"CapabilityStatement.profile.where(reference='{profile}').exists()"
                : $"{ResourceQuery("Account")}.supportedProfile.first()='{profile}'";
            var profileFound = (bool)statement.Scalar(fhirPath);
            Assert.True(profileFound);
        }

        private static string ResourceQuery(string resource)
        {
            return $"CapabilityStatement.rest.resource.where(type = '{resource}')";
        }
    }
}
