﻿using GoScript.Frontend.AST;
using GoScript.Frontend.Lex;
using GoScript.Frontend.Parse;
using GoScript.Frontend.Translation;

namespace GoScript.Frontend
{
    public static class Frontend
    {
        public static IEnumerable<Token> Lex(SourceFile file)
        {
            return new Lexer(file).Lex();
        }

        public static IEnumerable<ASTNode> Parse(IEnumerable<Token> tokens)
        {
            return new Parser(new TokenReader(tokens)).Parse();
        }

        public static IEnumerable<Statement> Translate(IEnumerable<ASTNode> asts)
        {
            return new Translator(asts).Translate();
        }
    }
}
