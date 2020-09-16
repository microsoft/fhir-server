// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FormatterConfiguration : IPostConfigureOptions<MvcOptions>
    {
        private readonly TextInputFormatter[] _inputFormatters;
        private readonly TextOutputFormatter[] _outputFormatters;

        public FormatterConfiguration(
            IOptions<FeatureConfiguration> featureConfiguration,
            IEnumerable<TextInputFormatter> inputFormatters,
            IEnumerable<TextOutputFormatter> outputFormatters)
        {
            EnsureArg.IsNotNull(featureConfiguration, nameof(featureConfiguration));
            EnsureArg.IsNotNull(featureConfiguration.Value, nameof(featureConfiguration));
            EnsureArg.IsNotNull(inputFormatters, nameof(inputFormatters));
            EnsureArg.IsNotNull(outputFormatters, nameof(outputFormatters));

            _inputFormatters = inputFormatters.ToArray();
            _outputFormatters = outputFormatters.ToArray();
        }

        public void PostConfigure(string name, MvcOptions options)
        {
            for (int i = 0; i < _inputFormatters.Length; i++)
            {
                options.InputFormatters.Insert(i, _inputFormatters[i]);
            }

            for (int i = 0; i < _outputFormatters.Length; i++)
            {
                options.OutputFormatters.Insert(i, _outputFormatters[i]);
            }

            // Disable the built-in global UnsupportedContentTypeFilter
            // We enable our own ValidateContentTypeFilterAttribute on the FhirController, the built-in filter
            // short-circuits the response and prevents the operation outcome from being returned.
            var unsupportedContentTypeFilter = options.Filters.Single(x => x is UnsupportedContentTypeFilter);
            options.Filters.Remove(unsupportedContentTypeFilter);
        }
    }
}
