// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Antlr4.Runtime.Misc;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterConflictingCodeValidatorTests
    {
        private readonly IModelInfoProvider _modelInfoProvider = new VersionSpecificModelInfoProvider();
        private readonly ILogger<SearchParameterConflictingCodeValidator> _logger = new NullLogger<SearchParameterConflictingCodeValidator>();
        private ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();

        private SearchParameterConflictingCodeValidator _validator;

        public SearchParameterConflictingCodeValidatorTests()
        {
        }

        [InlineData("ExplanationOfBenefit_Identifier_CDEX_baseResourceMismatch")]
        [InlineData("ExplanationOfBenefit_Identifier_CDEX_expressionMismatch")]
        [InlineData("ExplanationOfBenefit_Identifier_CDEX_expressionMismatch_Resource")]
        [InlineData("SearchParameterComponentDuplicate_mismatch")]
        [Theory]
        public void CheckForConflictingCodeValue_WithMismatch_AddsValidationFailure(string resourceFilePath)
        {
            // Arrange
            var searchParam = Samples.GetJsonSample<SearchParameter>(resourceFilePath);
            var validationFailures = new Collection<ValidationFailure>();
            var existingBaseTypes = new List<string> { "ExplanationOfBenefit" };
            var existingSearchParam = new SearchParameterInfo(
                "identifier",
                "identifier",
                ValueSets.SearchParamType.Token,
                url: new Uri("http://hl7.org/fhir/SearchParameter/ExplanationOfBenefit-identifier"),
                expression: "ExplanationOfBenefit.identifier",
                baseResourceTypes: existingBaseTypes.AsReadOnly());
            _searchParameterDefinitionManager.TryGetSearchParameter("ExplanationOfBenefit", searchParam.Code, out var searchParameterInfo)
                .Returns(x =>
                {
                    x[2] = existingSearchParam;
                    return true;
                });
            _searchParameterDefinitionManager.TryGetSearchParameter("Patient", searchParam.Code, out var searchParameterInfo2)
                .Returns(x =>
                {
                    x[2] = null;
                    return false;
                });

            var existingBaseTypes_Measure = new List<string> { "Measure" };
            var existingComponents = new List<SearchParameterComponentInfo>
            {
                new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/Measure-context-type"), "code"),
                new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/Measure-context-quantity"), "value.as(Quantity) | value.as(Range)"),
            };
            var existingSearchParam_Measure = new SearchParameterInfo(
                "context-type-quantity",
                "context-type-quantity",
                ValueSets.SearchParamType.Composite,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Measure-context-type-quantity"),
                expression: "Measure.useContext",
                baseResourceTypes: existingBaseTypes_Measure.AsReadOnly(),
                components: existingComponents.AsReadOnly());
            _searchParameterDefinitionManager.TryGetSearchParameter("Measure", searchParam.Code, out var searchParameterInfo3)
                .Returns(x =>
                {
                    x[2] = existingSearchParam_Measure;
                    return true;
                });

            _validator = new SearchParameterConflictingCodeValidator(_modelInfoProvider, _logger, _searchParameterDefinitionManager);

            // Act
            var dupOf = _validator.CheckForConflictingCodeValue(searchParam, validationFailures);

            // Assert
            Assert.Null(dupOf);
            Assert.NotEmpty(validationFailures);
        }

        [InlineData("ExplanationOfBenefit_Identifier_CDEX_dup", "http://hl7.org/fhir/SearchParameter/ExplanationOfBenefit-identifier")]
        [InlineData("SearchParameterComponentDuplicate", "http://hl7.org/fhir/SearchParameter/Measure-context-type-quantity")]
        [InlineData("SearchParameterUsCoreConditionCode", "http://hl7.org/fhir/SearchParameter/clinical-code")]
        [InlineData("SearchParameterUsCoreOrganizationName", "http://hl7.org/fhir/SearchParameter/Organization-name")]
        [InlineData("SearchParameterUsCoreRelatedPersonId", "http://hl7.org/fhir/SearchParameter/Resource-id")]
        [Theory]
        public void CheckForConflictingCodeValue_WithMatchingValues_MarksAsDuplicate(string resourceFilePath, string dupUrl)
        {
            // Arrange
            var searchParam = Samples.GetJsonSample<SearchParameter>(resourceFilePath);
            var validationFailures = new Collection<ValidationFailure>();
            var existingBaseTypes = new List<string> { "ExplanationOfBenefit" };
            var existingSearchParam = new SearchParameterInfo(
                "identifier",
                "identifier",
                ValueSets.SearchParamType.Token,
                url: new Uri(dupUrl),
                expression: "ExplanationOfBenefit.identifier",
                baseResourceTypes: existingBaseTypes.AsReadOnly());
            _searchParameterDefinitionManager.TryGetSearchParameter("ExplanationOfBenefit", searchParam.Code, out var searchParameterInfo)
                .Returns(x =>
                {
                    x[2] = existingSearchParam;
                    return true;
                });

            var existingBaseTypes_Measure = new List<string> { "Measure" };
            var existingComponents = new List<SearchParameterComponentInfo>
            {
                new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/Measure-context-type"), "code"),
                new SearchParameterComponentInfo(new Uri("http://hl7.org/fhir/SearchParameter/Measure-context-quantity"), "value.as(Quantity) | value.as(Range)"),
            };
            var existingSearchParam_Measure = new SearchParameterInfo(
                "context-type-quantity",
                "context-type-quantity",
                ValueSets.SearchParamType.Composite,
                url: new Uri(dupUrl),
                expression: "Measure.useContext",
                baseResourceTypes: existingBaseTypes_Measure.AsReadOnly(),
                components: existingComponents.AsReadOnly());
            _searchParameterDefinitionManager.TryGetSearchParameter("Measure", searchParam.Code, out var searchParameterInfo3)
                .Returns(x =>
                {
                    x[2] = existingSearchParam_Measure;
                    return true;
                });

            var existingBaseTypes_Code = new List<string>
            {
                "AllergyIntolerance",
                "Condition",
                "DeviceRequest",
                "DiagnosticReport",
                "FamilyMemberHistory",
                "List",
                "Medication",
                "MedicationAdministration",
                "MedicationDispense",
                "MedicationRequest",
                "MedicationStatement",
                "Observation",
                "Procedure",
                "ServiceRequest",
            };
            var existingSearchParam_Code = new SearchParameterInfo(
                "clinical-code",
                "code",
                ValueSets.SearchParamType.Token,
                url: new Uri(dupUrl),
                expression: "AllergyIntolerance.code | AllergyIntolerance.reaction.substance | Condition.code | (DeviceRequest.code as CodeableConcept) | DiagnosticReport.code | FamilyMemberHistory.condition.code | List.code | Medication.code | (MedicationAdministration.medication as CodeableConcept) | (MedicationDispense.medication as CodeableConcept) | (MedicationRequest.medication as CodeableConcept) | (MedicationStatement.medication as CodeableConcept) | Observation.code | Procedure.code | ServiceRequest.code",
                baseResourceTypes: existingBaseTypes_Code.AsReadOnly());
            _searchParameterDefinitionManager.TryGetSearchParameter("Condition", searchParam.Code, out var searchParameterInfo4)
                .Returns(x =>
                {
                    x[2] = existingSearchParam_Code;
                    return true;
                });

            var existingBaseTypes_Name = new List<string> { "Organization" };
            var existingSearchParam_Name = new SearchParameterInfo(
                "Organization-name",
                "name",
                ValueSets.SearchParamType.String,
                url: new Uri(dupUrl),
                expression: "Organization.name | Organization.alias",
                baseResourceTypes: existingBaseTypes_Name.AsReadOnly());
            _searchParameterDefinitionManager.TryGetSearchParameter("Organization", searchParam.Code, out var searchParameterInfo5)
                .Returns(x =>
                {
                    x[2] = existingSearchParam_Name;
                    return true;
                });

            var existingBaseTypes_Id = new List<string> { "Resource" };
            var existingSearchParam_Id = new SearchParameterInfo(
                "_id",
                "_id",
                ValueSets.SearchParamType.Token,
                url: new Uri(dupUrl),
                expression: "Resource.id",
                baseResourceTypes: existingBaseTypes_Id.AsReadOnly());
            _searchParameterDefinitionManager.TryGetSearchParameter("RelatedPerson", searchParam.Code, out var searchParameterInfo6)
                .Returns(x =>
                {
                    x[2] = existingSearchParam_Id;
                    return true;
                });

            _validator = new SearchParameterConflictingCodeValidator(_modelInfoProvider, _logger, _searchParameterDefinitionManager);

            // Act
            var dupOf = _validator.CheckForConflictingCodeValue(searchParam, validationFailures);

            // Assert
            Assert.Equal(new Uri(dupUrl), dupOf);
        }
    }
}
