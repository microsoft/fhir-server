// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FhirXmlOutputFormatter : TextOutputFormatter
    {
        private readonly FhirXmlSerializer _fhirXmlSerializer;
        private readonly ILogger<FhirXmlOutputFormatter> _logger;
        private readonly ResourceDeserializer _deserializer;

        public FhirXmlOutputFormatter(FhirXmlSerializer fhirXmlSerializer, ResourceDeserializer deserializer, ILogger<FhirXmlOutputFormatter> logger)
        {
            EnsureArg.IsNotNull(fhirXmlSerializer, nameof(fhirXmlSerializer));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));

            _fhirXmlSerializer = fhirXmlSerializer;
            _logger = logger;
            _deserializer = deserializer;

            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);

            SupportedMediaTypes.Add(KnownContentTypes.XmlContentType);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationXml);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.TextXml);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationAnyXmlSyntax);
        }

        protected override bool CanWriteType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            return typeof(Resource).IsAssignableFrom(type) || typeof(RawResourceElement).IsAssignableFrom(type);
        }

        public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

            context.HttpContext.AllowSynchronousIO();

            Resource resourceObject = null;
            if (typeof(RawResourceElement).IsAssignableFrom(context.ObjectType))
            {
                resourceObject = _deserializer.Deserialize(context.Object as RawResourceElement).ToPoco<Resource>();
            }
            else if (typeof(Hl7.Fhir.Model.Bundle).IsAssignableFrom(context.ObjectType))
            {
                // Need to set Resource property for resources in entries
                var bundle = context.Object as Hl7.Fhir.Model.Bundle;

                foreach (var entry in bundle.Entry.Where(x => x is RawBundleEntryComponent))
                {
                    var rawResource = entry as RawBundleEntryComponent;
                    entry.Resource = _deserializer.Deserialize(rawResource.ResourceElement).ToPoco<Resource>();
                }

                resourceObject = bundle;
            }
            else
            {
                resourceObject = (Resource)context.Object;
            }

            HttpResponse response = context.HttpContext.Response;
            using (TextWriter textWriter = context.WriterFactory(response.Body, selectedEncoding))
            using (var writer = new XmlTextWriter(textWriter))
            {
                if (context.HttpContext.GetPrettyOrDefault())
                {
                    writer.Formatting = Formatting.Indented;
                }

                _fhirXmlSerializer.Serialize(resourceObject, writer, context.HttpContext.GetSummaryTypeOrDefault(), elements: context.HttpContext.GetElementsOrDefault());
            }

            return Task.CompletedTask;
        }
    }
}
