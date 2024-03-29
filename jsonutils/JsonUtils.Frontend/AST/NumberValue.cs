﻿namespace JsonUtils.Frontend.AST
{
    public sealed class NumberValue : JsonObject
    {
        public string Value { get; init; }
        public SourceLocation Location { get; init; }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override void Accept(IVisitor visitor)
        {
            visitor.Visit(this);
        }

        public NumberValue(string value, SourceLocation location)
        {
            Value = value;
            Location = location;
        }
    }
}
