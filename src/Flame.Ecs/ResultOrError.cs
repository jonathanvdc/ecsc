using System;
using System.Diagnostics;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that is either a result or an error, and never
    /// both.
    /// </summary>
    public struct ResultOrError<TResult, TError>
    {
        private ResultOrError(TResult Result, TError Error, bool IsError)
        {
            this.result = Result;
            this.err = Error;
            this.IsError = IsError;
        }

        private TResult result;
        private TError err;

        /// <summary>
        /// Creates a result-or-error instance that represents a result.
        /// </summary>
        public static ResultOrError<TResult, TError> FromResult(TResult Result)
        {
            return new ResultOrError<TResult, TError>(Result, default(TError), false);
        }

        /// <summary>
        /// Creates a result-or-error instance that represents an error.
        /// </summary>
        public static ResultOrError<TResult, TError> FromError(TError Error)
        {
            return new ResultOrError<TResult, TError>(default(TResult), Error, true);
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents an error.
        /// </summary>
        /// <value><c>true</c> if this instance is an error; otherwise, <c>false</c>.</value>
        public bool IsError { get; private set; }

        /// <summary>
        /// Gets the result.
        /// </summary>
        /// <value>The result.</value>
        public TResult Result 
        { 
            get 
            {
                Debug.Assert(!IsError, "!IsError");
                return result;
            }
        }

        /// <summary>
        /// Gets the result, or the default value for TResult, if this instance is an error.
        /// </summary>
        /// <value>The result or the default value.</value>
        public TResult ResultOrDefault
        {
            get
            {
                return result;
            }
        }

        /// <summary>
        /// Gets the error.
        /// </summary>
        /// <value>The error.</value>
        public TError Error 
        {
            get
            {
                Debug.Assert(IsError, "IsError");
                return err;
            }
        }

        /// <summary>
        /// Gets the error, or the default value for TError, if this instance is not an error.
        /// </summary>
        /// <value>The error or the default value.</value>
        public TError ErrorOrDefault
        {
            get
            {
                return err;
            }
        }

        /// <summary>
        /// Applies the given mapping function to this instance's result.
        /// </summary>
        public ResultOrError<TResult2, TError> MapResult<TResult2>(Func<TResult, TResult2> Map)
        {
            if (IsError)
                return ResultOrError<TResult2, TError>.FromError(Error);
            else
                return ResultOrError<TResult2, TError>.FromResult(Map(Result));
        }

        /// <summary>
        /// If this instance is not an error, then the given function is applied
        /// to the result; otherwise, the current error is propagated. 
        /// </summary>
        public ResultOrError<TResult2, TError> BindResult<TResult2>(Func<TResult, ResultOrError<TResult2, TError>> Map)
        {
            if (IsError)
                return ResultOrError<TResult2, TError>.FromError(Error);
            else
                return Map(Result);
        }

        /// <summary>
        /// Applies one of the given functions, and returns the result.
        /// </summary>
        public T Apply<T>(Func<TResult, T> ResultMap, Func<TError, T> ErrorMap)
        {
            return IsError
                ? ErrorMap(Error)
                : ResultMap(Result);
        }
    }
}

