using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;

namespace Flame.Ecs
{
    /// <summary>
    /// Defines a value that has been analyzed.
    /// Conceptually, a value can be an expression
    /// or a variable.
    /// It supports retrieving the value itself,
    /// its address, and storing a value in the value,
    /// but whether these operations are successful
    /// depends on the context.
    /// </summary>
    public interface IValue
    {
        /// <summary>
        /// Gets this value's type. This is the type of the
        /// value returned by a get-expression for this value,
        /// and also the type of any and all values that are
        /// stored in this value.
        /// </summary>
        /// <value>The value's type.</value>
        IType Type { get; }

        /// <summary>
        /// Creates an expression that retrieves this
        /// variable's address.
        /// </summary>
        /// <param name="Scope">The local scope in which the address-of-expression is created.</param>
        /// <param name="Location">The source location that is highlighted by log entries.</param>
        ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(ILocalScope Scope, SourceLocation Location);

        /// <summary>
        /// Creates an expression that retrieves this
        /// variable's value.
        /// </summary>
        /// <param name="Scope">The local scope in which the get-expression is created.</param>
        /// <param name="Location">The source location that is highlighted by log entries.</param>
        ResultOrError<IExpression, LogEntry> CreateGetExpression(ILocalScope Scope, SourceLocation Location);

        /// <summary>
        /// Creates an expression that stores a value in this
        /// variable.
        /// </summary>
        /// <param name="Scope">The local scope in which the set-statement is created.</param>
        /// <param name="Location">The source location that is highlighted by log entries.</param>
        ResultOrError<IStatement, LogEntry> CreateSetStatement(IExpression Value, ILocalScope Scope, SourceLocation Location);
    }

    public static class ValueExtensions
    {
        /// <summary>
        /// Creates an expression that retrieves this
        /// variable's value. If anything goes wrong, then
        /// the error is immediately logged.
        /// </summary>
        /// <param name="Scope">The local scope in which the get-expression is created.</param>
        /// <param name="Location">The source location that is highlighted by log entries.</param>
        public static IExpression CreateGetExpressionOrError(
            this IValue Value, ILocalScope Scope, SourceLocation Location)
        {
            return Value.CreateGetExpression(Scope, Location).Apply(
                expr => expr,
                error =>
                {
                    Scope.Function.Global.Log.LogError(error);
                    return new UnknownExpression(Value.Type);
                });
        }

        /// <summary>
        /// Creates an expression that retrieves this
        /// variable's value. If anything goes wrong, then
        /// the error is immediately logged.
        /// </summary>
        /// <param name="Scope">The local scope in which the get-expression is created.</param>
        /// <param name="Location">The source location that is highlighted by log entries.</param>
        public static IStatement CreateSetStatementOrError(
            this IValue Variable, IExpression Value, ILocalScope Scope, SourceLocation Location)
        {
            return Variable.CreateSetStatement(Value, Scope, Location)
                .ResultOrLog(Scope.Function.Global.Log);
        }

        public static IExpression ResultOrLog(
            this ResultOrError<IExpression, LogEntry> Result, ICompilerLog Log)
        {
            return Result.Apply(
                expr => expr,
                error =>
                {
                    Log.LogError(error);
                    return ExpressionConverters.ErrorTypeExpression;
                });
        }

        public static IStatement ResultOrLog(
            this ResultOrError<IStatement, LogEntry> Result, ICompilerLog Log)
        {
            return Result.Apply(
                expr => expr,
                error =>
                {
                    Log.LogError(error);
                    return EmptyStatement.Instance;
                });
        }
    }
}

