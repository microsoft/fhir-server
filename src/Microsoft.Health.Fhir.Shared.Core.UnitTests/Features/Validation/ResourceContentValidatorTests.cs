// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Validate)]
public class ResourceContentValidatorTests
{
    private readonly IModelAttributeValidator _modelAttributeValidator;
    private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
    private readonly IFhirRequestContext _fhirRequestContext;
    private readonly ResourceElement _sample = Samples.GetJsonSample("Profile-Patient-uscore");

    public ResourceContentValidatorTests()
    {
        _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        _requestContextAccessor.RequestContext.Returns(_fhirRequestContext);

        _modelAttributeValidator = Substitute.For<IModelAttributeValidator>();
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithNoHeader_ThenRecursiveValidationIsDisabled()
    {
        _fhirRequestContext.RequestHeaders.Returns(new Dictionary<string, StringValues>());
        _modelAttributeValidator.TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), Arg.Any<bool>())
            .Returns(true);

        var validator = new ResourceContentValidator(_modelAttributeValidator, _requestContextAccessor);
        var result = validator.Validate(_sample);

        Assert.True(result.IsValid);
        _modelAttributeValidator.Received(1).TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), false);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithRecursiveHeaderSetToTrue_ThenRecursiveValidationIsEnabled()
    {
        var headers = new Dictionary<string, StringValues>
        {
            { KnownHeaders.RecursiveValidation, "true" },
        };
        _fhirRequestContext.RequestHeaders.Returns(headers);
        _modelAttributeValidator.TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), Arg.Any<bool>())
            .Returns(true);

        var validator = new ResourceContentValidator(_modelAttributeValidator, _requestContextAccessor);
        var result = validator.Validate(_sample);

        Assert.True(result.IsValid);
        _modelAttributeValidator.Received(1).TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), true);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithRecursiveHeaderSetToFalse_ThenRecursiveValidationIsDisabled()
    {
        var headers = new Dictionary<string, StringValues>
        {
            { KnownHeaders.RecursiveValidation, "false" },
        };
        _fhirRequestContext.RequestHeaders.Returns(headers);
        _modelAttributeValidator.TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), Arg.Any<bool>())
            .Returns(true);

        var validator = new ResourceContentValidator(_modelAttributeValidator, _requestContextAccessor);
        var result = validator.Validate(_sample);

        Assert.True(result.IsValid);
        _modelAttributeValidator.Received(1).TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), false);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithInvalidHeaderValue_ThenRecursiveValidationDefaultsToDisabled()
    {
        var headers = new Dictionary<string, StringValues>
        {
            { KnownHeaders.RecursiveValidation, "invalid" },
        };
        _fhirRequestContext.RequestHeaders.Returns(headers);
        _modelAttributeValidator.TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), Arg.Any<bool>())
            .Returns(true);

        var validator = new ResourceContentValidator(_modelAttributeValidator, _requestContextAccessor);
        var result = validator.Validate(_sample);

        Assert.True(result.IsValid);
        _modelAttributeValidator.Received(1).TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), false);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithoutContextAccessor_ThenRecursiveValidationDefaultsToDisabled()
    {
        _modelAttributeValidator.TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), Arg.Any<bool>())
            .Returns(true);

        var validator = new ResourceContentValidator(_modelAttributeValidator);
        var result = validator.Validate(_sample);

        Assert.True(result.IsValid);
        _modelAttributeValidator.Received(1).TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), false);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithNullRequestContext_ThenRecursiveValidationDefaultsToDisabled()
    {
        _requestContextAccessor.RequestContext.Returns((IFhirRequestContext)null);
        _modelAttributeValidator.TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), Arg.Any<bool>())
            .Returns(true);

        var validator = new ResourceContentValidator(_modelAttributeValidator, _requestContextAccessor);
        var result = validator.Validate(_sample);

        Assert.True(result.IsValid);
        _modelAttributeValidator.Received(1).TryValidate(Arg.Any<ResourceElement>(), Arg.Any<ICollection<ValidationResult>>(), false);
    }
}
