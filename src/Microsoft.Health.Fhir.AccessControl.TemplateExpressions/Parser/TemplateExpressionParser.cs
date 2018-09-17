// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using static Superpower.Parse;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Parser
{
    /// <summary>
    /// A template expression parser, working on a token list from <see cref="TemplateExpressionTokenizer"/>.
    /// </summary>
    internal static class TemplateExpressionParser
    {
        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> Call =
            (from name in Token.EqualTo(TemplateExpressionToken.Identifier)
             from lParen in Token.EqualTo(TemplateExpressionToken.LParen)
             from arguments in Ref(() => Expression).ManyDelimitedBy(Token.EqualTo(TemplateExpressionToken.Comma))
             from rParen in Token.EqualTo(TemplateExpressionToken.RParen)
             select (TemplateExpression)new CallTemplateExpression(name.Span.UntilEndOf(rParen.Span), name.ToStringValue(), arguments))
            .Named("function call");

        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> Number =
            from minus in Token.EqualTo(TemplateExpressionToken.Minus).OptionalOrDefault()
            from number in Token.EqualTo(TemplateExpressionToken.Number)
            let absoluteNumber = int.Parse(
                number.Span.Source.AsSpan(number.Span.Position.Absolute, number.Span.Length),
                NumberStyles.Number,
                CultureInfo.InvariantCulture)
            select (TemplateExpression)(minus.HasValue
                    ? new NumericLiteralTemplateExpression(minus.Span.UntilEndOf(number.Span), -absoluteNumber)
                    : new NumericLiteralTemplateExpression(number.Span, absoluteNumber));

        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> StringLiteralSegment =
            Token.EqualTo(TemplateExpressionToken.StringLiteralSegment)
                .Apply(textSpan =>
                    from chars in Character.ExceptIn('\'', '{', '\\')
                        .Or(Character.EqualTo('\\')
                            .IgnoreThen(
                                Character.EqualTo('\\')
                                    .Or(Character.EqualTo('\'')
                                        .Or(Character.EqualTo('{')))
                                    .Named("escape sequence")))
                        .Many()
                    select (TemplateExpression)new StringLiteralTemplateExpression(textSpan.Span, new string(chars)));

        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> StringInterpolationSegment =
            (from lBrace in Token.EqualTo(TemplateExpressionToken.LBrace)
             from expression in Ref(() => Expression)
             from rBrace in Token.EqualTo(TemplateExpressionToken.RBrace)
             select (TemplateExpression)new InterpolationTemplateExpression(lBrace.Span.UntilEndOf(rBrace.Span), expression))
            .Named("interpolation");

        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> UnquotedString =
            StringLiteralSegment
                .Or(StringInterpolationSegment)
                .AtLeastOnce()
                .Select(e => (TemplateExpression)new InterpolatedStringTemplateExpression(
                    e.First().TextSpan.UntilEndOf(e.Last().TextSpan),
                    RemoveInterpolations(e)));

        private static readonly InterpolatedStringTemplateExpression EmptyInterpolatedStringTemplateExpression =
            new InterpolatedStringTemplateExpression(
                TextSpan.Empty,
                new[] { new StringLiteralTemplateExpression(TextSpan.Empty, string.Empty) });

        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> QuotedString =
            (from lQuote in Token.EqualTo(TemplateExpressionToken.Quote)
             from str in UnquotedString.OptionalOrDefault(EmptyInterpolatedStringTemplateExpression)
             from rQuote in Token.EqualTo(TemplateExpressionToken.Quote)
             select (TemplateExpression)new InterpolatedStringTemplateExpression(lQuote.Span.UntilEndOf(rQuote.Span), ((InterpolatedStringTemplateExpression)str).Segments))
            .Named("quoted string");

        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> Expression =
            Number.Or(QuotedString).Or(Call).Named("expression");

        private static readonly TokenListParser<TemplateExpressionToken, TemplateExpression> Template =
            UnquotedString.OptionalOrDefault(EmptyInterpolatedStringTemplateExpression).AtEnd();

        /// <summary>
        /// Tokenizes and parses a template expression tree.
        /// </summary>
        /// <param name="input">The input expression string.</param>
        /// <param name="diagnostics">The diagnostics collection.</param>
        /// <returns>A template expression AST, if the input is syntactically valid. Otherwise, returns null with entries in the <paramref name="diagnostics"/> collection.</returns>
        public static TemplateExpression Parse(string input, TemplateExpressionDiagnosticCollection diagnostics)
        {
            EnsureArg.IsNotNull(input, nameof(input));
            EnsureArg.IsNotNull(diagnostics, nameof(diagnostics));

            Result<TokenList<TemplateExpressionToken>> tokens = TemplateExpressionTokenizer.Instance.TryTokenize(input);
            if (!tokens.HasValue)
            {
                diagnostics.Add(tokens.Location, tokens.FormatErrorMessageFragment());
                return null;
            }

            TokenListParserResult<TemplateExpressionToken, TemplateExpression> parsed = Template.TryParse(tokens.Value);
            if (!parsed.HasValue)
            {
                TextSpan errorSpan;
                Position parsedErrorPosition = parsed.ErrorPosition;
                if (parsedErrorPosition.Absolute >= input.Length)
                {
                    errorSpan = new TextSpan(input, parsedErrorPosition, 0);
                }
                else
                {
                    int absoluteErrorPosition = parsedErrorPosition.Absolute;
                    Token<TemplateExpressionToken> errorToken = tokens.Value.FirstOrDefault(t => absoluteErrorPosition >= t.Span.Position.Absolute && absoluteErrorPosition < t.Span.Position.Absolute + t.Span.Length);
                    errorSpan = errorToken.Span;
                }

                diagnostics.Add(errorSpan, parsed.FormatErrorMessageFragment());
                return null;
            }

            return parsed.Value;
        }

        private static TemplateExpression[] RemoveInterpolations(TemplateExpression[] templateExpressions)
        {
            for (var i = 0; i < templateExpressions.Length; i++)
            {
                var templateExpression = templateExpressions[i];
                if (templateExpression is InterpolationTemplateExpression interpolation)
                {
                    templateExpressions[i] = interpolation.InnerExpression;
                }
            }

            return templateExpressions;
        }

        /// <summary>
        /// This is a transient expression type that is created in <see cref="TemplateExpressionParser.StringInterpolationSegment"/>
        ///  in order to capture the full text span of the interpolation, not just that of the inner expression.
        /// </summary>
        private class InterpolationTemplateExpression : TemplateExpression
        {
            public InterpolationTemplateExpression(TextSpan textSpan, TemplateExpression innerExpression)
                : base(textSpan)
            {
                EnsureArg.IsNotNull(innerExpression, nameof(innerExpression));
                InnerExpression = innerExpression;
            }

            public TemplateExpression InnerExpression { get; }

            public override TResult Accept<TContext, TResult>(TemplateExpressionVisitor<TContext, TResult> templateExpressionVisitor, TContext context) => throw new NotSupportedException();
        }
    }
}
