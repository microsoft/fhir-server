// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Validate)]
public class ProfileValidatorTests
{
    private readonly IProvideProfilesForValidation _profilesResolver;
    private readonly IOptions<ValidateOperationConfiguration> _options;
    private readonly ILogger<ProfileValidator> _logger;

    public ProfileValidatorTests()
    {
        _profilesResolver = Substitute.For<IProvideProfilesForValidation>();
        _logger = Substitute.For<ILogger<ProfileValidator>>();

        var config = new ValidateOperationConfiguration
        {
            CacheDurationInSeconds = 60,
            MaxExpansionSize = 5000,
        };
        _options = Options.Create(config);
    }

    [SkippableFact]
    public void GivenR4OrR4BFhirVersion_WhenCreatingValidator_ThenCid0ConstraintIsIgnored()
    {
        Skip.If(
            ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
            ModelInfoProvider.Instance.Version != FhirSpecification.R4B,
            "This test is only valid for R4 and R4B");

        // Arrange - cid-0 spec error only in R4 and R4B (fixed in R5+)
        var validator = new ProfileValidator(_profilesResolver, _options, _logger, ModelInfoProvider.Instance);

        // Act
        var internalValidator = validator.GetValidator();

        // Assert
        Assert.Contains("cid-0", internalValidator.Settings.ConstraintsToIgnore ?? []);
    }

    [SkippableFact]
    public void GivenStu3OrR5FhirVersion_WhenCreatingValidator_ThenCid0ConstraintIsNotIgnored()
    {
        Skip.If(
            ModelInfoProvider.Instance.Version != FhirSpecification.Stu3 &&
            ModelInfoProvider.Instance.Version != FhirSpecification.R5,
            "This test is only valid for STU3 and R5");

        // Arrange - cid-0 spec error does not apply to STU3 (no ChargeItemDefinition) or R5 (issue fixed)
        var validator = new ProfileValidator(_profilesResolver, _options, _logger, ModelInfoProvider.Instance);

        // Act
        var internalValidator = validator.GetValidator();

        // Asser
        Assert.DoesNotContain("cid-0", internalValidator.Settings.ConstraintsToIgnore ?? []);
    }
}
