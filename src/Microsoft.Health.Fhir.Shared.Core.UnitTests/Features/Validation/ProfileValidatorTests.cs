// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
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

    [Fact]
    public void GivenABundleWithAnEntryThatHasNoResource_WhenValidating_ThenNoInternalLogicFailureIsReported()
    {
        // Arrange
        var validator = new ProfileValidator(_profilesResolver, _options, _logger, ModelInfoProvider.Instance);
        Bundle bundle = CreateTransactionBundleWithAResourcelessEntry();

        // Act
        OperationOutcomeIssue[] issues = validator.TryValidate(bundle.ToTypedElement());

        // Assert - ordinary validation issues are expected and fine, an internal failure is not.
        Assert.DoesNotContain(issues, IsCatastrophicFailure);
    }

    private static bool IsCatastrophicFailure(OperationOutcomeIssue issue)
    {
        if (issue.DetailsText?.Contains("Internal logic failure", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return string.Equals(issue.Severity, "Fatal", StringComparison.OrdinalIgnoreCase)
            && issue.DetailsCodes?.Coding.Any(coding => string.Equals(coding.Code, "5003", StringComparison.Ordinal)) == true;
    }

    private static Bundle CreateTransactionBundleWithAResourcelessEntry()
    {
        const string organizationFullUrl = "urn:uuid:6e2b8f22-6f2c-4a5f-9bd2-0f5f8c0a0001";

        var bundle = new Bundle
        {
            Type = Bundle.BundleType.Transaction,
        };

        bundle.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = "urn:uuid:6e2b8f22-6f2c-4a5f-9bd2-0f5f8c0a0002",
            Resource = new Patient
            {
                ManagingOrganization = new ResourceReference(organizationFullUrl),
            },
            Request = new Bundle.RequestComponent
            {
                Method = Bundle.HTTPVerb.POST,
                Url = "Patient",
            },
        });

        bundle.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = organizationFullUrl,
            Resource = new Organization
            {
                Name = "Contoso Health",
            },
            Request = new Bundle.RequestComponent
            {
                Method = Bundle.HTTPVerb.POST,
                Url = "Organization",
            },
        });

        // The trigger: a transaction DELETE entry has a request but no resource.
        bundle.Entry.Add(new Bundle.EntryComponent
        {
            Request = new Bundle.RequestComponent
            {
                Method = Bundle.HTTPVerb.DELETE,
                Url = "Patient/does-not-exist",
            },
        });

        return bundle;
    }
}
