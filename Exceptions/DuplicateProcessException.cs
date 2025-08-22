using System;

namespace CloudlogHelper.Exceptions;

/// <summary>
///     Reports when duplicate application process is detected.
/// </summary>
public class DuplicateProcessException : InvalidOperationException
{
    public DuplicateProcessException()
    {
    }

    public DuplicateProcessException(string message) : base(message)
    {
    }

    public DuplicateProcessException(string message, Exception inner) : base(message, inner)
    {
    }
}