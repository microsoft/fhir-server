// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Dom.Events;
using AngleSharp.Html.Parser;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;

namespace Microsoft.Health.Fhir.Core.Features.Validation.Narratives
{
    public class NarrativeHtmlSanitizer : INarrativeHtmlSanitizer
    {
        private readonly ILogger _logger;

        private static readonly HashSet<string> AllowedElements = new(StringComparer.OrdinalIgnoreCase)
        {
            // https://www.hl7.org/fhir/narrative-definitions.html#Narrative.div
            "a",
            "abbr",
            "acronym",
            "b",
            "big",
            "blockquote",
            "br",
            "caption",
            "cite",
            "code",
            "col",
            "colgroup",
            "dd",
            "dfn",
            "div",
            "dl",
            "dt",
            "em",
            "h1",
            "h2",
            "h3",
            "h4",
            "h5",
            "h6",
            "hr",
            "i",
            "img",
            "li",
            "ol",
            "p",
            "pre",
            "q",
            "samp",
            "small",
            "span",
            "strong",
            "sub",
            "sup",
            "table",
            "tbody",
            "td",
            "tfoot",
            "th",
            "thead",
            "tr",
            "tt",
            "ul",
            "var",
        };

        private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            // https://www.hl7.org/fhir/narrative-definitions.html#Narrative.div
            "abbr",
            "accesskey",
            "align",
            "alt",
            "axis",
            "bgcolor",
            "border",
            "cellhalign",
            "cellpadding",
            "cellspacing",
            "cellvalign",
            "char",
            "charoff",
            "charset",
            "cite",
            "class",
            "colspan",
            "compact",
            "coords",
            "dir",
            "frame",
            "headers",
            "height",
            "href",
            "hreflang",
            "hspace",
            "id",
            "lang",
            "longdesc",
            "name",
            "nowrap",
            "rel",
            "rev",
            "rowspan",
            "rules",
            "scope",
            "shape",
            "span",
            "src",
            "start",
            "style",
            "summary",
            "tabindex",
            "title",
            "type",
            "valign",
            "value",
            "vspace",
            "width",
            "xml:lang",
            "xmlns",
        };

        private static readonly ISet<string> Src = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#",
            "data:",
            "http:",
            "https:",
        };

        // Obvious invalid structural parsing errors to report
        private static readonly HashSet<HtmlParseError> RaiseErrorTypes = new HashSet<HtmlParseError>
        {
            HtmlParseError.AmbiguousOpenTag,
            HtmlParseError.BogusComment,
            HtmlParseError.CharacterReferenceInvalidCode,
            HtmlParseError.ClosingSlashMisplaced,
            HtmlParseError.EOF,
            HtmlParseError.TagClosingMismatch,
            HtmlParseError.TagDoesNotMatchCurrentNode,
        };

        private const string HtmlTemplate = "<!DOCTYPE html><body>{0}</body>";

        public NarrativeHtmlSanitizer(ILogger<NarrativeHtmlSanitizer> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        /// <summary>
        /// Validates that the provided HTML uses only Elements and Attributes specified in the FHIR specification
        /// </summary>
        /// <param name="html">The raw input HTML</param>
        /// <returns>An enumeration of errors</returns>
        public IEnumerable<string> Validate(string html)
        {
            EnsureArg.IsNotNullOrEmpty(html, nameof(html));

            using (_logger.BeginTimedScope($"{nameof(NarrativeHtmlSanitizer)}.{nameof(Validate)}"))
            {
                var errors = new List<HtmlErrorEvent>();
                var options = new HtmlParserOptions { IsStrictMode = false };
                var parser = new HtmlParser(options);

                parser.Error += (sender, error) => errors.Add((HtmlErrorEvent)error);

                var dom = parser.ParseDocument(string.Format(HtmlTemplate, html));

                // Report parsing errors
                if (errors.Any())
                {
                    foreach (var error in errors.Where(x => RaiseErrorTypes.Contains((HtmlParseError)x.Code)))
                    {
                        yield return string.Format(Core.Resources.IllegalHtmlParsingError, error.Message, error.Position.Line, error.Position.Column);
                    }

                    yield break;
                }

                var htmlBodyElement = dom.Children.OfType<IHtmlHtmlElement>().FirstOrDefault()
                    ?.Children.OfType<IHtmlBodyElement>().FirstOrDefault();

                // According to https://www.hl7.org/fhir/narrative.html
                // the provided html must be contained within a <div> element.
                // Here we check the Body element has exactly 1 child that is a Div

                if (htmlBodyElement?.Children?.Length != 1
                    || !(htmlBodyElement.Children?.FirstOrDefault() is IHtmlDivElement containerDiv))
                {
                    yield return Core.Resources.IllegalHtmlOuterDiv;
                    yield break;
                }

                // Div element must have some non-whitespace content
                if (string.IsNullOrWhiteSpace(containerDiv.InnerHtml))
                {
                    yield return Core.Resources.IllegalHtmlEmpty;
                    yield break;
                }

                var invalidHtml = new List<string>();

                CheckHtmlElements(
                    containerDiv,
                    el => invalidHtml.Add(string.Format(Core.Resources.IllegalHtmlElement, el.NodeName)),
                    (el, attr) => invalidHtml.Add(string.Format(Core.Resources.IllegalHtmlAttribute, attr.Name, el.NodeName)));

                foreach (var htmlError in invalidHtml)
                {
                    yield return htmlError;
                }
            }
        }

        /// <summary>
        /// Cleanses the provided HTML to return only Elements and Attributes allowed by the FHIR specification
        /// </summary>
        /// <param name="html">The raw HTML</param>
        /// <returns>Sanitized HTML for display purposes</returns>
        public string Sanitize(string html)
        {
            EnsureArg.IsNotNullOrEmpty(html, nameof(html));

            using (_logger.BeginTimedScope($"{nameof(NarrativeHtmlSanitizer)}.{nameof(Sanitize)}"))
            {
                var parser = new HtmlParser();

                using (var dom = parser.ParseDocument(string.Format(HtmlTemplate, html)))
                {
                    var containerDiv = dom.Body.Children.OfType<IHtmlDivElement>().FirstOrDefault();
                    if (containerDiv == null)
                    {
                        return string.Empty;
                    }

                    CheckHtmlElements(
                        containerDiv,
                        el => el.Replace(el.ChildNodes.ToArray()),
                        (el, attr) => el.RemoveAttribute(attr.Name));

                    dom.Normalize();

                    return containerDiv.ToHtml(HtmlMarkupFormatter.Instance);
                }
            }
        }

        private static void CheckHtmlElements(
            IHtmlDivElement htmlDivElement,
            Action<IElement> onInvalidElement,
            Action<IElement, IAttr> onInvalidAttr)
        {
            EnsureArg.IsNotNull(htmlDivElement, nameof(htmlDivElement));
            EnsureArg.IsNotNull(onInvalidElement, nameof(onInvalidElement));
            EnsureArg.IsNotNull(onInvalidAttr, nameof(onInvalidAttr));

            ValidateAttributes(htmlDivElement, onInvalidAttr);

            // Ensure only allowed elements and attributes are used
            foreach (IElement element in htmlDivElement.QuerySelectorAll("*"))
            {
                if (!AllowedElements.Contains(element.NodeName))
                {
                    onInvalidElement(element);
                }

                ValidateAttributes(element, onInvalidAttr);
            }
        }

        private static void ValidateAttributes(IElement element, Action<IElement, IAttr> onInvalidAttr)
        {
            foreach (IAttr attr in element.Attributes.ToArray())
            {
                if (!AllowedAttributes.Contains(attr.Name))
                {
                    onInvalidAttr(element, attr);
                }

                if (string.Equals("src", attr.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!Src.Any(x => attr.Value.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                    {
                        onInvalidAttr(element, attr);
                    }
                }
            }
        }
    }
}
