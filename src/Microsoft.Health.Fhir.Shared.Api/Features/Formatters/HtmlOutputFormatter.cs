// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Text;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Models;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    public class HtmlOutputFormatter : TextOutputFormatter
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly ILogger<HtmlOutputFormatter> _logger;
        private readonly INarrativeHtmlSanitizer _htmlSanitizer;
        private IArrayPool<char> _charPool;

        public HtmlOutputFormatter(
            FhirJsonSerializer fhirJsonSerializer,
            ILogger<HtmlOutputFormatter> logger,
            INarrativeHtmlSanitizer htmlSanitizer,
            ArrayPool<char> charPool)
        {
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(htmlSanitizer, nameof(htmlSanitizer));
            EnsureArg.IsNotNull(charPool, nameof(charPool));

            _fhirJsonSerializer = fhirJsonSerializer;
            _logger = logger;
            _htmlSanitizer = htmlSanitizer;
            _charPool = new JsonArrayPool(charPool);

            SupportedEncodings.Add(Encoding.UTF8);
            SupportedMediaTypes.Add("text/html");
            SupportedMediaTypes.Add("application/xhtml+xml");
        }

        protected override bool CanWriteType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            return typeof(Resource).IsAssignableFrom(type);
        }

        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

            var engine = context.HttpContext.RequestServices.GetService<IRazorViewEngine>();
            var tempDataProvider = context.HttpContext.RequestServices.GetService<ITempDataProvider>();

            var actionContext = new ActionContext(context.HttpContext, context.HttpContext.GetRouteData(), new ActionDescriptor());

            using (TextWriter textWriter = context.WriterFactory(context.HttpContext.Response.Body, selectedEncoding))
            {
                var viewName = "ViewJson";
                var viewResult = engine.FindView(actionContext, viewName, true);

                if (viewResult.View == null)
                {
                    throw new FileNotFoundException(Api.Resources.ViewNotFound, $"{viewName}.cshtml");
                }

                var resourceInstance = (Resource)context.Object;
                string div = null;

                if (resourceInstance is DomainResource domainResourceInstance && !string.IsNullOrEmpty(domainResourceInstance.Text?.Div))
                {
                    div = _htmlSanitizer.Sanitize(domainResourceInstance.Text.Div);
                }

                var stringBuilder = new StringBuilder();
                using (var stringWriter = new StringWriter(stringBuilder))
                using (var jsonTextWriter = new JsonTextWriter(stringWriter))
                {
                    jsonTextWriter.ArrayPool = _charPool;
                    jsonTextWriter.Formatting = Formatting.Indented;

                    await _fhirJsonSerializer.SerializeAsync(resourceInstance, jsonTextWriter, context.HttpContext.GetSummaryTypeOrDefault(), context.HttpContext.GetElementsOrDefault());
                }

                var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    Model = new CodePreviewModel
                    {
                        Code = stringBuilder.ToString(),
                        Div = div,
                    },
                };

                var viewContext = new ViewContext(
                    actionContext,
                    viewResult.View,
                    viewDictionary,
                    new TempDataDictionary(actionContext.HttpContext, tempDataProvider),
                    textWriter,
                    new HtmlHelperOptions());

                await viewResult.View.RenderAsync(viewContext);
            }
        }
    }
}
