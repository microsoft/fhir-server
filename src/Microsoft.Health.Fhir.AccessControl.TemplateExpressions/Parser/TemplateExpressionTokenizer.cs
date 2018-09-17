// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Parser
{
    /// <summary>
    /// Tokenizes (lexes) a template expression string.
    /// </summary>
    internal class TemplateExpressionTokenizer : Tokenizer<TemplateExpressionToken>
    {
        public static readonly TemplateExpressionTokenizer Instance = new TemplateExpressionTokenizer();

        private static readonly TemplateExpressionToken[] SimpleTokens = CreateSimpleTokens();

        private enum StringState
        {
            OutsideLiteralSegment,
            BeginningOfLiteralSegment,
            EndOfLiteralSegment,
        }

        private static TextParser<Unit> Identifier { get; } =
            Character.Matching(c => !char.IsWhiteSpace(c) && SimpleTokens[c] == 0, "identifier").Value(Unit.Value).IgnoreMany();

        private static TextParser<Unit> StringLiteralSegment { get; } =
            Character.ExceptIn('\'', '{', '\\')
                .Or(Character.EqualTo('\\')
                    .IgnoreThen(
                        Character.EqualTo('\\')
                            .Or(Character.EqualTo('\'')
                                .Or(Character.EqualTo('{')))))
                .IgnoreMany();

        protected override IEnumerable<Result<TemplateExpressionToken>> Tokenize(TextSpan span)
        {
            var next = span.ConsumeChar();
            if (!next.HasValue)
            {
                yield break;
            }

            var stringState = StringState.BeginningOfLiteralSegment;

            do
            {
                if (stringState == StringState.BeginningOfLiteralSegment)
                {
                    var literalSegment = StringLiteralSegment(next.Location);
                    if (!literalSegment.HasValue)
                    {
                        yield return Result.CastEmpty<Unit, TemplateExpressionToken>(literalSegment);
                    }
                    else if (literalSegment.Remainder.Position.Absolute - literalSegment.Location.Position.Absolute > 0)
                    {
                        yield return Result.Value(TemplateExpressionToken.StringLiteralSegment, literalSegment.Location, literalSegment.Remainder);
                    }

                    next = literalSegment.Remainder.ConsumeChar();
                    stringState = StringState.EndOfLiteralSegment;
                }
                else
                {
                    next = SkipWhiteSpace(next.Location);
                    var simpleToken = SimpleTokens[next.Value];
                    if (next.Value < SimpleTokens.Length && simpleToken != TemplateExpressionToken.None)
                    {
                        yield return Result.Value(simpleToken, next.Location, next.Remainder);
                        next = next.Remainder.ConsumeChar();
                        switch (stringState)
                        {
                            case StringState.OutsideLiteralSegment:
                                switch (simpleToken)
                                {
                                    case TemplateExpressionToken.Quote:
                                        stringState = StringState.BeginningOfLiteralSegment;
                                        break;
                                    case TemplateExpressionToken.RBrace:
                                        stringState = StringState.BeginningOfLiteralSegment;
                                        break;
                                }

                                break;
                            case StringState.BeginningOfLiteralSegment:
                                throw new InvalidOperationException("Invalid parser state");
                            case StringState.EndOfLiteralSegment:
                                switch (simpleToken)
                                {
                                    case TemplateExpressionToken.LBrace:
                                    case TemplateExpressionToken.Quote:
                                        stringState = StringState.OutsideLiteralSegment;
                                        break;
                                }

                                break;
                        }
                    }
                    else if (char.IsDigit(next.Value))
                    {
                        var integer = Numerics.Integer(next.Location);
                        yield return Result.Value(TemplateExpressionToken.Number, integer.Location, integer.Remainder);
                        next = integer.Remainder.ConsumeChar();
                    }
                    else
                    {
                        var identifier = Identifier(next.Location);
                        yield return Result.Value(TemplateExpressionToken.Identifier, identifier.Location, identifier.Remainder);
                        next = identifier.Remainder.ConsumeChar();
                    }
                }
            }
            while (next.HasValue);
        }

        private static TemplateExpressionToken[] CreateSimpleTokens()
        {
            var tokens = new TemplateExpressionToken[128];
            tokens['('] = TemplateExpressionToken.LParen;
            tokens[')'] = TemplateExpressionToken.RParen;
            tokens['{'] = TemplateExpressionToken.LBrace;
            tokens['}'] = TemplateExpressionToken.RBrace;
            tokens[','] = TemplateExpressionToken.Comma;
            tokens['-'] = TemplateExpressionToken.Minus;
            tokens['\''] = TemplateExpressionToken.Quote;
            return tokens;
        }
    }
}
