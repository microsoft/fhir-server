// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FhirJsonOutputFormatter : TextOutputFormatter
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer;
        private readonly ResourceDeserializer _deserializer;
        private readonly ILogger<FhirJsonOutputFormatter> _logger;
        private readonly IArrayPool<char> _charPool;
        private readonly BundleSerializer _bundleSerializer;

        public FhirJsonOutputFormatter(
            FhirJsonSerializer fhirJsonSerializer,
            ResourceDeserializer deserializer,
            ILogger<FhirJsonOutputFormatter> logger,
            ArrayPool<char> charPool,
            BundleSerializer bundleSerializer)
        {
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(charPool, nameof(charPool));
            EnsureArg.IsNotNull(bundleSerializer, nameof(bundleSerializer));

            _fhirJsonSerializer = fhirJsonSerializer;
            _deserializer = deserializer;
            _logger = logger;
            _charPool = new JsonArrayPool(charPool);
            _bundleSerializer = bundleSerializer;

            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);
            SupportedMediaTypes.Add(KnownContentTypes.JsonContentType);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationJson);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.TextJson);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationAnyJsonSyntax);
        }

        protected override bool CanWriteType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            return typeof(Resource).IsAssignableFrom(type) || typeof(RawResourceElement).IsAssignableFrom(type);
        }

        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

            context.HttpContext.AllowSynchronousIO();

            HttpResponse response = context.HttpContext.Response;

            var elementsSearchParameter = context.HttpContext.GetElementsSearchParameter(_logger);

            Resource resource = null;

            if (context.Object is Hl7.Fhir.Model.Bundle)
            {
                var bundle = context.Object as Hl7.Fhir.Model.Bundle;
                resource = bundle;

                if (elementsSearchParameter?.Any() == true || !bundle.Entry.All(x => x is RawBundleEntryComponent))
                {
                    // _elements is not supported for a raw resource, revert to using FhirJsonSerializer
                    foreach (var rawBundleEntryComponent in bundle.Entry)
                    {
                        if (rawBundleEntryComponent is RawBundleEntryComponent)
                        {
                            rawBundleEntryComponent.Resource = ((RawBundleEntryComponent)rawBundleEntryComponent).ResourceElement.ToPoco<Resource>(_deserializer);
                        }
                    }
                }
                else
                {
                    await _bundleSerializer.Serialize(context.Object as Hl7.Fhir.Model.Bundle, context.HttpContext.Response.Body);
                    return;
                }
            }
            else if (context.Object is RawResourceElement)
            {
                if (elementsSearchParameter != null && elementsSearchParameter.Any())
                {
                    // _elements is not supported for a raw resource, revert to using FhirJsonSerializer
                    resource = (context.Object as RawResourceElement).ToPoco<Resource>(_deserializer);
                }
                else
                {
                    await (context.Object as RawResourceElement).SerializeToStreamAsUtf8Json(context.HttpContext.Response.Body);
                    return;
                }
            }
            else
            {
                resource = (Resource)context.Object;
            }

            using (TextWriter textWriter = context.WriterFactory(response.Body, selectedEncoding))
            using (var jsonWriter = new JsonTextWriter(textWriter))
            {
                jsonWriter.ArrayPool = _charPool;

                if (context.HttpContext.GetIsPretty())
                {
                    jsonWriter.Formatting = Formatting.Indented;
                }

                _fhirJsonSerializer.Serialize(resource, jsonWriter, context.HttpContext.GetSummaryType(_logger), elementsSearchParameter);
                await jsonWriter.FlushAsync();
            }
        }
    }
}
