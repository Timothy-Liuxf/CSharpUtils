﻿using GoScript.Frontend.AST;
using GoScript.Frontend.Lex;
using GoScript.Frontend.Types;
using GoScript.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GoScript.Frontend.Parse
{
    internal class Parser
    {
        private bool parsed = false;

        public IEnumerable<ASTNode> Parse()
        {
            if (parsed)
            {
                throw new InternalErrorException("Already parsed.");
            }

            parsed = true;
            while (tokens.CurrentToken != null)
            {
                yield return ParseStatement();
            }
        }

        private Statement ParseStatement()
        {
            var currToken = tokens.CurrentToken;
            // if currToken is Keyword, get the type of the keyword
            if (currToken?.TokenCatagory == TokenType.Keyword)
            {
                switch ((currToken as Keyword)!.Type)
                {
                    case KeywordType.Var:
                        return ParseVarDeclStmt();
                    case KeywordType.If:
                        return ParseIfStmt();
                    case KeywordType.For:
                        return ParseForStmt();
                    case KeywordType.Break:
                        return ParseBreakStmt();
                    case KeywordType.Continue:
                        return ParseContinueStmt();
                    case KeywordType.Return:
                        return ParseReturnStmt();
                }
            }

            if (tokens.TryPeekPunctuator(PunctuatorType.LBrace, out _))
            {
                return ParseCompoundStmt();
            }

            return ParseAssignOrExprStmt();
        }

        private VarDeclStmt ParseVarDeclStmt()
        {
            var varDecl = ParseVarDecl();
            tokens.TryMatchPunctuator(PunctuatorType.Semicolon, out _);
            tokens.MatchNewline();
            return new VarDeclStmt(varDecl, varDecl.Location);
        }

        private VarDecl ParseVarDecl()
        {
            VarDecl varDecl;
            var location = tokens.MatchKeyword(KeywordType.Var).Location;
            var identifiers = new List<Identifier>() { tokens.MatchIdentifier() };
            while (tokens.TryMatchPunctuator(PunctuatorType.Comma, out _))
            {
                identifiers.Add(tokens.MatchIdentifier());
            }

            if (!TryParseType(out var type)
                || tokens.TryMatchPunctuator(PunctuatorType.Assign, out _))
            {
                if (type is null)
                {
                    tokens.MatchPunctuator(PunctuatorType.Assign);
                }

                var initExprs = ParseNonEmptyExprList();

                if (type is null)
                {
                    varDecl = new VarDecl(
                        identifiers.Select((identifier, _) => (identifier.Name, identifier.Location)),
                        initExprs, location
                    );
                }
                else
                {
                    varDecl = new VarDecl(
                        identifiers.Select((identifier, _) => (identifier.Name, identifier.Location)),
                        type.Value, initExprs, location
                    );
                }
            }
            else
            {
                varDecl = new VarDecl(
                    identifiers.Select((identifier, _) => (identifier.Name, identifier.Location)),
                    type.Value, location
                );
            }

            return varDecl;
        }

        private IReadOnlyList<Expression> ParseNonEmptyExprList()
        {
            var exprs = new List<Expression>() { ParseExpression() };
            while (tokens.TryMatchPunctuator(PunctuatorType.Comma, out _))
            {
                exprs.Add(ParseExpression());
            }
            return exprs;
        }

        private bool TryParseType([MaybeNullWhen(false), NotNullWhen(true)] out (GSType, SourceLocation)? type)
        {
            if (tokens.TryMatchTypeKeyword(out var typeKeyword))
            {
                type = (GSBasicType.ParseBasicType(typeKeyword.Type)
                    ?? throw new InternalErrorException(
                        $"Internal error at {typeKeyword.Location}: cannot parse type keyword {Keyword.GetKeywordString(typeKeyword.Type)}.")
                    , typeKeyword.Location);
                return true;
            }
            if (tokens.TryMatchKeyword(KeywordType.Func, out var func))
            {
                tokens.MatchPunctuator(PunctuatorType.LParen);
                var paramTypes = ParseTypeList();
                tokens.MatchPunctuator(PunctuatorType.RParen);

                if (tokens.TryMatchPunctuator(PunctuatorType.LParen, out _))
                {
                    var returnTypes = ParseTypeList();
                    tokens.MatchPunctuator(PunctuatorType.RParen);
                    type = (new GSFuncType(
                            paramType: paramTypes.Select((paramType, _) => paramType.Item1),
                            returnType: returnTypes.Select((returnType, _) => returnType.Item1)
                        ), func.Location);
                }
                else if (TryParseType(out var returnType))
                {
                    type = (new GSFuncType(
                            paramType: paramTypes.Select((paramType, _) => paramType.Item1),
                            returnType: new List<GSType> { returnType.Value.Item1 }
                        ), func.Location);
                }
                else
                {
                    type = (new GSFuncType(
                            paramType: paramTypes.Select((paramType, _) => paramType.Item1),
                            returnType: new List<GSType>()
                        ), func.Location);
                }
                return true;
            }
            type = null;
            return false;
        }

        private IReadOnlyList<(GSType, SourceLocation)> ParseTypeList()
        {
            var typeList = new List<(GSType, SourceLocation)>();
            if (TryParseType(out var type))
            {
                typeList.Add(type.Value);
                while (tokens.TryMatchPunctuator(PunctuatorType.Comma, out var comma))
                {
                    if (!TryParseType(out type))
                    {
                        throw new SyntaxErrorException(comma.Location, "Expected type after comma \',\'.");
                    }
                    typeList.Add(type.Value);
                }
            }
            return typeList;
        }

        private IfStmt ParseIfStmt()
        {
            var location = tokens.MatchKeyword(KeywordType.If).Location;
            var condBranches = new List<(Expression, Compound, SourceLocation)>();
            var cond = ParseExpression();
            var branch = ParseCompound();
            condBranches.Add((cond, branch, location));
            while (tokens.TryMatchKeyword(KeywordType.Else, out var @else))
            {
                if (tokens.TryMatchKeyword(KeywordType.If, out var @if))
                {
                    // else if
                    cond = ParseExpression();
                    branch = ParseCompound();
                    condBranches.Add((cond, branch, @if.Location));
                }
                else
                {
                    // else
                    branch = ParseCompound();
                    tokens.MatchNewline();
                    return new IfStmt(condBranches, branch, location);
                }
            }
            tokens.MatchNewline();
            return new IfStmt(condBranches, location);
        }

        private ForStmt ParseForStmt()
        {
            var location = tokens.MatchKeyword(KeywordType.For).Location;
            if (tokens.TryPeekPunctuator(PunctuatorType.LBrace, out _))
            {
                return new ForStmt(ParseCompoundStmt(), location);
            }

            var initStmt = ParseAssignOrExpr();
            if (tokens.TryMatchPunctuator(PunctuatorType.Semicolon, out _))
            {
                var condition = ParseExpression();
                tokens.MatchPunctuator(PunctuatorType.Semicolon);
                var postStmt = ParseAssignOrExpr();
                if (postStmt is DefAssignStmt)
                {
                    throw new SyntaxErrorException(
                        $"At {postStmt.Location}: Cannot declare in post statement of the for loop.");
                }
                return new ForStmt(initStmt, condition, postStmt, ParseCompoundStmt(), location);
            }
            else
            {
                if (initStmt is not SingleStmt)
                {
                    throw new SyntaxErrorException(initStmt.Location, $"Cannot use {initStmt} as value");
                }
                var condition = (initStmt as SingleStmt)!.Expr;
                return new ForStmt(condition, ParseCompoundStmt(), location);
            }
        }

        private BreakStmt ParseBreakStmt()
        {
            var @break = tokens.MatchKeyword(KeywordType.Break);
            tokens.TryMatchPunctuator(PunctuatorType.Semicolon, out _);
            tokens.MatchNewline();
            return new BreakStmt(@break.Location);
        }

        private ContinueStmt ParseContinueStmt()
        {
            var @continue = tokens.MatchKeyword(KeywordType.Continue);
            tokens.TryMatchPunctuator(PunctuatorType.Semicolon, out _);
            tokens.MatchNewline();
            return new ContinueStmt(@continue.Location);
        }

        private CompoundStmt ParseCompoundStmt()
        {
            var compound = ParseCompound();
            tokens.MatchNewline();
            return new CompoundStmt(compound, compound.Location);
        }

        private Compound ParseCompound()
        {
            var location = tokens.MatchPunctuator(PunctuatorType.LBrace).Location;
            tokens.MatchNewline();
            var statements = new List<Statement>();
            while (!tokens.TryMatchPunctuator(PunctuatorType.RBrace, out _))
            {
                statements.Add(ParseStatement());
            }
            return new Compound(statements, location);
        }

        private Statement ParseAssignOrExprStmt()
        {
            var statement = ParseAssignOrExpr();
            statement.EndWithNewLine = true;
            tokens.TryMatchPunctuator(PunctuatorType.Semicolon, out _);
            tokens.MatchNewline();
            return statement;
        }

        private Statement ParseAssignOrExpr()
        {
            if (tokens.TryPeekPunctuator(PunctuatorType.Semicolon, out var emptySemicolon))
            {
                return new EmptyStmt(emptySemicolon.Location);
            }
            if (tokens.TryPeekNewline(out var emptyNewline))
            {
                return new EmptyStmt(emptyNewline.Location);
            }

            var expr = ParseExpression();
            if (tokens.TryPeekPunctuator(PunctuatorType.Comma, out var comma)
                || tokens.TryPeekPunctuator(PunctuatorType.Assign, out _)
                || tokens.TryPeekPunctuator(PunctuatorType.DefAssign, out _))
            {
                // AssignStmt
                var assignedExprs = new List<Expression> { expr };
                if (comma is not null)
                {
                    while (tokens.TryMatchPunctuator(PunctuatorType.Comma, out _))
                    {
                        assignedExprs.Add(ParseExpression());
                    }
                }

                // Match = or :=
                if (!tokens.TryMatchPunctuator(PunctuatorType.Assign, out var assign))
                {
                    assign = tokens.MatchPunctuator(PunctuatorType.DefAssign);
                }

                var assigneeExprs = ParseNonEmptyExprList();

                if (assign.Type == PunctuatorType.Assign)
                {
                    // '='
                    return new AssignStmt(assignedExprs, assigneeExprs, assign.Location);
                }
                else
                {
                    // ':='
                    var assignedNames = assignedExprs.Select((assignedExpr, _) =>
                    {
                        return assignedExpr.IsIdExpr ? (assignedExpr as IdExpr)!.Name
                            : throw new InvalidOperationException(
                                $"At {assignedExpr.Location}: Non-name {assignedExpr} on left side of :=");
                    }).ToList();
                    return new DefAssignStmt(assignedNames, assigneeExprs, assign.Location);
                }
            }
            else if (!tokens.TryPeekPunctuator(PunctuatorType.Semicolon, out _))
            {
                return new SingleStmt(expr, true, expr.Location);
            }
            else
            {
                return new SingleStmt(expr, false, expr.Location);
            }
        }

        private Expression ParseExpression()
        {
            if (tokens.TryPeekKeyword(KeywordType.Func, out _))
            {
                return ParseFuncExpr();
            }
            return ParseLogicalOrExpr();
        }

        private Expression ParseLogicalOrExpr()
        {
            var expr = ParseLogicalAndExpr();
            while (tokens.TryMatchPunctuator(PunctuatorType.Or, out var op))
            {
                var rExpr = ParseLogicalAndExpr();
                expr = new LogicalOrExpr(expr, rExpr, op.Location);
            }
            return expr;
        }

        private Expression ParseLogicalAndExpr()
        {
            var expr = ParseComparisonExpr();
            while (tokens.TryMatchPunctuator(PunctuatorType.And, out var op))
            {
                var rExpr = ParseAdditiveExpr();
                expr = new LogicalAndExpr(expr, rExpr, op.Location);
            }
            return expr;
        }

        private Expression ParseComparisonExpr()
        {
            var expr = ParseAdditiveExpr();
            while (tokens.TryMatchPunctuator(PunctuatorType.Equal, out var op)
                || tokens.TryMatchPunctuator(PunctuatorType.NotEqual, out op)
                || tokens.TryMatchPunctuator(PunctuatorType.Greater, out op)
                || tokens.TryMatchPunctuator(PunctuatorType.Less, out op)
                || tokens.TryMatchPunctuator(PunctuatorType.GreaterEq, out op)
                || tokens.TryMatchPunctuator(PunctuatorType.LessEq, out op))
            {
                var rExpr = ParseAdditiveExpr();
                expr = new ComparisonExpr(expr, rExpr, op.Type switch
                {
                    PunctuatorType.Equal => ComparisonExpr.OperatorType.Equ,
                    PunctuatorType.NotEqual => ComparisonExpr.OperatorType.Neq,
                    PunctuatorType.Greater => ComparisonExpr.OperatorType.Gre,
                    PunctuatorType.Less => ComparisonExpr.OperatorType.Les,
                    PunctuatorType.GreaterEq => ComparisonExpr.OperatorType.Geq,
                    PunctuatorType.LessEq => ComparisonExpr.OperatorType.Leq,
                    _ => throw new InternalErrorException($"Unexpected operator {op.Type}"),
                }, op.Location);
            }
            return expr;
        }

        private Expression ParseAdditiveExpr()
        {
            var expr = ParseMultiplicativeExpr();
            while (tokens.TryMatchPunctuator(PunctuatorType.Add, out var op)
                || tokens.TryMatchPunctuator(PunctuatorType.Sub, out op))
            {
                var rExpr = ParseMultiplicativeExpr();
                expr = new AdditiveExpr(expr, rExpr, op.Type switch
                {
                    PunctuatorType.Add => AdditiveExpr.OperatorType.Add,
                    PunctuatorType.Sub => AdditiveExpr.OperatorType.Sub,
                    _ => throw new InternalErrorException("Unexpected operator type."),
                },
                op.Location);
            }
            return expr;
        }

        private Expression ParseMultiplicativeExpr()
        {
            var expr = ParseUnaryExpr();
            while (tokens.TryMatchPunctuator(PunctuatorType.Star, out var op)
                || tokens.TryMatchPunctuator(PunctuatorType.Div, out op)
                || tokens.TryMatchPunctuator(PunctuatorType.Mod, out op))
            {
                var rExpr = ParseUnaryExpr();
                expr = new MultiplicativeExpr(expr, rExpr, op.Type switch
                {
                    PunctuatorType.Star => MultiplicativeExpr.OperatorType.Mul,
                    PunctuatorType.Div => MultiplicativeExpr.OperatorType.Div,
                    PunctuatorType.Mod => MultiplicativeExpr.OperatorType.Mod,
                    _ => throw new InternalErrorException("Unexpected operator type."),
                },
                op.Location);
            }
            return expr;
        }

        private Expression ParseUnaryExpr()
        {
            if (tokens.TryMatchPunctuator(PunctuatorType.Sub, out var negOp))
            {
                var expr = ParseUnaryExpr();
                return new UnaryExpr(UnaryExpr.OperatorType.Neg, expr, negOp.Location);
            }
            if (tokens.TryMatchPunctuator(PunctuatorType.Not, out var notOp))
            {
                var expr = ParseUnaryExpr();
                return new UnaryExpr(UnaryExpr.OperatorType.Not, expr, notOp.Location);
            }
            return ParsePrimaryExpr();
        }

        private Expression ParsePrimaryExpr()
        {
            if (tokens.TryMatchIdentifier(out var identifier))
            {
                return new IdExpr(identifier.Name, identifier.Location);
            }
            if (tokens.TryMatchLiteral(LiteralType.IntegerLiteral, out var integerLiteral))
            {
                return new IntegerConstantExpr((integerLiteral as IntegerLiteral)!.Value, integerLiteral.Location);
            }
            if (tokens.TryMatchLiteral(LiteralType.BoolLiteral, out var boolLiteral))
            {
                return new BoolConstantExpr((boolLiteral as BoolLiteral)!.Value, boolLiteral.Location);
            }
            if (tokens.TryMatchPunctuator(PunctuatorType.LParen, out _))
            {
                var expr = ParseExpression();
                tokens.MatchPunctuator(PunctuatorType.RParen);
                return expr;
            }
            throw new NotImplementedException($"ParsePrimaryExpr at {tokens.CurrentToken?.Location.ToString() ?? "EOF"}.");
        }

        private FuncExpr ParseFuncExpr()
        {
            tokens.MatchKeyword(KeywordType.Func);
            return ParseFuncSignature();
        }

        private FuncExpr ParseFuncSignature()
        {
            var func = tokens.MatchPunctuator(PunctuatorType.LParen);
            var @params = new List<(GSType, string, SourceLocation)>();
            var untypedParams = new List<Identifier>();
            while (tokens.TryMatchIdentifier(out var paramName))
            {
                if (TryParseType(out var paramType))
                {
                    foreach (var untypedName in untypedParams)
                    {
                        @params.Add((paramType.Value.Item1, untypedName.Name, untypedName.Location));
                    }
                    untypedParams.Clear();
                    @params.Add((paramType.Value.Item1, paramName.Name, paramName.Location));
                }
                else
                {
                    untypedParams.Add(paramName);
                }
                if (tokens.TryMatchPunctuator(PunctuatorType.Comma, out _))
                {
                }
                else if (tokens.TryPeekPunctuator(PunctuatorType.RParen, out _))
                {
                    break;
                }
                else
                {
                    var currentToken = tokens.CurrentToken;
                    if (currentToken is null)
                    {
                        throw new SyntaxErrorException(paramType is null ? paramName.Location : paramType.Value.Item2,
                            $"Missing token ',' or ')'.");
                    }
                    else
                    {
                        throw new SyntaxErrorException(currentToken.Location,
                            $"Expected token ',' or ')', found {currentToken}.");
                    }
                }
            }

            var rParen = tokens.MatchPunctuator(PunctuatorType.RParen);
            if (untypedParams.Count > 0)
            {
                throw new SyntaxErrorException(rParen.Location, $"Missing type.");
            }

            IReadOnlyList<(GSType, SourceLocation)> returnTypes;
            if (tokens.TryMatchPunctuator(PunctuatorType.LParen, out _))
            {
                returnTypes = ParseTypeList();
                tokens.MatchPunctuator(PunctuatorType.RParen);
            }
            else if (TryParseType(out var returnType))
            {
                returnTypes = new List<(GSType, SourceLocation)>() { returnType.Value };
            }
            else
            {
                returnTypes = new List<(GSType, SourceLocation)>();
            }
            var body = ParseCompound();

            return new FuncExpr(body, @params, returnTypes, func.Location);
        }

        private ReturnStmt ParseReturnStmt()
        {
            var @return = tokens.MatchKeyword(KeywordType.Return);
            if (tokens.TryMatchPunctuator(PunctuatorType.Semicolon, out _)
                || tokens.TryPeekNewline(out _))
            {
                tokens.MatchNewline();
                return new ReturnStmt(new List<Expression>(), @return.Location);
            }
            var returnExpr = ParseNonEmptyExprList();
            tokens.TryMatchPunctuator(PunctuatorType.Semicolon, out _);
            tokens.MatchNewline();
            return new ReturnStmt(returnExpr, @return.Location);
        }

        private readonly TokenReader tokens;

        public Parser(TokenReader tokens)
        {
            this.tokens = tokens;
        }
    }
}
