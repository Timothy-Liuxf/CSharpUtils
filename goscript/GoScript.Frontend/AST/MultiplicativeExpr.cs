﻿using GoScript.Utils;

namespace GoScript.Frontend.AST
{
    public sealed class MultiplicativeExpr : ArithmeticExpression
    {
        public enum OperatorType
        {
            Mul,
            Div,
            Mod,
        }

        public OperatorType Operator { get; private init; }

        public MultiplicativeExpr(Expression lExpr, Expression rExpr, OperatorType @operator, SourceLocation location)
            : base(lExpr, rExpr, location)
        {
            this.Operator = @operator;
        }

        public override string ToString()
        {
            var ch = this.Operator switch
            {
                OperatorType.Mul => '*',
                OperatorType.Div => '/',
                OperatorType.Mod => '%',
                _ => throw new InternalErrorException("Unknown operator type."),
            };
            return $"{this.LExpr} {ch} {this.RExpr}";
        }

        internal override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
