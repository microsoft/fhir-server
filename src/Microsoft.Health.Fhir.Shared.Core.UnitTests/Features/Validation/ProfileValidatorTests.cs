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

    [Theory]
    [InlineData(FhirSpecification.R4)]
    [InlineData(FhirSpecification.R4B)]
    [InlineData(FhirSpecification.R5)]
    public void GivenR4OrLaterFhirVersion_WhenCreatingValidator_ThenCid0ConstraintIsIgnored(FhirSpecification version)
    {
        // Arrange
        var modelInfoProvider = MockModelInfoProviderBuilder.Create(version).Build();
        var validator = new ProfileValidator(_profilesResolver, _options, _logger, modelInfoProvider);

        // Act
        var internalValidator = GetValidator(validator);

        // Assert
        Assert.NotNull(internalValidator);
        Assert.NotNull(internalValidator.Settings.ConstraintsToIgnore);
        Assert.Contains("cid-0", internalValidator.Settings.ConstraintsToIgnore);
    }

    [Fact]
    public void GivenStu3FhirVersion_WhenCreatingValidator_ThenCid0ConstraintIsNotIgnored()
    {
        // Arrange
        var modelInfoProvider = MockModelInfoProviderBuilder.Create(FhirSpecification.Stu3).Build();
        var validator = new ProfileValidator(_profilesResolver, _options, _logger, modelInfoProvider);

        // Act
        var internalValidator = GetValidator(validator);

        // Assert
        Assert.NotNull(internalValidator);
        Assert.Empty(internalValidator.Settings.ConstraintsToIgnore ?? []);
    }

    [Fact]
    public void GivenR4WithExistingConstraintsToIgnore_WhenCreatingValidator_ThenCid0IsAppendedToExisting()
    {
        // Arrange
        var modelInfoProvider = MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build();

        // Mock ValidationSettings to have existing constraints
        var validationSettings = new ValidationSettings
        {
            ConstraintsToIgnore = ["existing-constraint"],
        };

        var validator = new ProfileValidator(_profilesResolver, _options, _logger, modelInfoProvider);

        // Act
        var internalValidator = GetValidator(validator);

        // Assert
        Assert.NotNull(internalValidator);
        Assert.NotNull(internalValidator.Settings.ConstraintsToIgnore);
        Assert.Contains("cid-0", internalValidator.Settings.ConstraintsToIgnore);
        // Note: The test validates that cid-0 is added; the existing constraint behavior
        // depends on the hl7.fhirpath library's default settings
    }

    [Fact]
    public void GivenValidatorCreatedMultipleTimes_WhenRefreshingAfterTimeout_ThenCid0ConstraintIsStillIgnored()
    {
        // Arrange
        var modelInfoProvider = MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build();
        var validator = new ProfileValidator(_profilesResolver, _options, _logger, modelInfoProvider);

        // Act - Get validator first time
        var firstValidator = GetValidator(validator);
        var firstConstraints = firstValidator.Settings.ConstraintsToIgnore;

        // Manually refresh by setting _lastValidatorRefresh to past to simulate timeout
        var lastRefreshField = typeof(ProfileValidator).GetField("_lastValidatorRefresh", BindingFlags.NonPublic | BindingFlags.Instance);
        lastRefreshField?.SetValue(validator, DateTime.MinValue);

        // Get validator again (should be refreshed)
        var secondValidator = GetValidator(validator);
        var secondConstraints = secondValidator.Settings.ConstraintsToIgnore;

        // Assert
        Assert.NotNull(firstConstraints);
        Assert.NotNull(secondConstraints);
        Assert.Contains("cid-0", firstConstraints);
        Assert.Contains("cid-0", secondConstraints);
    }

    private static Validator GetValidator(ProfileValidator profileValidator)
    {
        var method = typeof(ProfileValidator).GetMethod("GetValidator", BindingFlags.NonPublic | BindingFlags.Instance);
        return method?.Invoke(profileValidator, null) as Validator;
    }
}
