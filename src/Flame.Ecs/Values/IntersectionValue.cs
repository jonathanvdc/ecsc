using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value implementation that is the intersection
    /// of two other values.
    /// </summary>
    public class IntersectionValue : IValue
    {
        public IntersectionValue(IValue First, IValue Second) 
        {
            this.First = First;
            this.Second = Second;
        }

        public IValue First { get; private set; }
        public IValue Second { get; private set; }

        private static IValue Create(IValue[] ValArray, int Index)
        {
            if (Index == ValArray.Length - 1)
                return ValArray[Index];
            else
                return new IntersectionValue(
                    ValArray[Index], 
                    Create(ValArray, Index + 1));
        }

        /// <summary>
        /// Creates an intersection value from the given sequence
        /// of values.
        /// </summary>
        public static IValue Create(IEnumerable<IValue> Values)
        {
            var vals = Values.ToArray();
            if (vals.Length == 0)
                return null;
            else
                return Create(vals, 0);
        }

        /// <inheritdoc/>
        public IType Type { get { return new IntersectionType(First.Type, Second.Type); } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            var fst = First.CreateGetExpression(Scope, Location);
            var snd = Second.CreateGetExpression(Scope, Location);
            if (fst.IsError)
                return snd;
            else if (snd.IsError)
                return fst;
            else
                return ResultOrError<IExpression, LogEntry>.FromResult(
                    new IntersectionExpression(fst.Result, snd.Result));
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            var fst = First.CreateAddressOfExpression(Scope, Location);
            var snd = Second.CreateAddressOfExpression(Scope, Location);
            if (fst.IsError)
                return snd;
            else if (snd.IsError)
                return fst;
            else
                return ResultOrError<IExpression, LogEntry>.FromResult(
                    new IntersectionExpression(fst.Result, snd.Result));
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            var fst = First.CreateSetStatement(Value, Scope, Location);
            var snd = Second.CreateSetStatement(Value, Scope, Location);
            if (fst.IsError)
                return snd;
            else if (snd.IsError)
                return fst;
            else
                return ResultOrError<IStatement, LogEntry>.FromError(
                    new LogEntry(
                        "invalid operation",
                        "cannot assign a value to this expression, because it is ambiguous.",
                        Location));
        }
    }
}

