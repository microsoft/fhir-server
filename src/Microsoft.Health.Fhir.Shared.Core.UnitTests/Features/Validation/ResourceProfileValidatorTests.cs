// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentValidation.Results;
using Hl7.Fhir.ElementModel;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Validate)]
public class ResourceProfileValidatorTests
{
    private readonly ResourceProfileValidator _validator;
    private readonly IProfileValidator _profileValidator;
    private readonly IFhirRequestContext _fhirRequestContext;
    private readonly OperationOutcomeIssue _operationOutcomeWarningIssue = new(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Invalid, detailsText: "Warning text.");
    private readonly Dictionary<string, StringValues> _strictHeader = new Dictionary<string, StringValues> { { "Prefer", "handling=strict" } };
    private readonly ResourceElement _sample = Samples.GetJsonSample("Profile-Patient-uscore");

    public ResourceProfileValidatorTests()
    {
        RequestContextAccessor<IFhirRequestContext> requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        requestContextAccessor.RequestContext.Returns(_fhirRequestContext);

        _profileValidator = Substitute.For<IProfileValidator>();
        _validator = new ResourceProfileValidator(
        Substitute.For<IModelAttributeValidator>(),
        _profileValidator,
        requestContextAccessor,
        true);
    }

    [Fact]
    public void GivenAResource_WhenValidatingNoIssues_ThenTheValidationResultSucceeds()
    {
        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(Array.Empty<OperationOutcomeIssue>());

        ValidationResult result = _validator.Validate(_sample);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithAWarning_ThenTheValidationResultSucceeds()
    {
        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { _operationOutcomeWarningIssue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.True(result.IsValid);
    }

    [Theory]
    [MemberData(nameof(ErrorIssues))]
    public void GivenAResource_WhenValidatingWithAnError_ThenTheValidationResultFails(OperationOutcomeIssue issue)
    {
        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { issue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithAWarningAndStrictHandling_ThenTheValidationResultFails()
    {
        _fhirRequestContext.RequestHeaders.Returns(_strictHeader);

        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { _operationOutcomeWarningIssue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.False(result.IsValid);
    }

    [Theory]
    [MemberData(nameof(ErrorIssues))]
    public void GivenAResource_WhenValidatingWithAnErrorAndStrictHandling_ThenTheValidationResultFails(OperationOutcomeIssue issue)
    {
        _fhirRequestContext.RequestHeaders.Returns(_strictHeader);

        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { issue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.False(result.IsValid);
    }

    public static IEnumerable<object[]> ErrorIssues()
    {
        yield return new object[] { new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Invalid, detailsText: "Error text.") };
        yield return new object[] { new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Fatal, OperationOutcomeConstants.IssueType.Invalid, detailsText: "Fatal text.") };
    }
}
