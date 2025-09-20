using System;

namespace CloudlogHelper.Exceptions;

public class InvalidPollException : InvalidOperationException
{
    public InvalidPollException()
    {
    }

    public InvalidPollException(string message) : base(message)
    {
    }

    public InvalidPollException(string message, Exception inner) : base(message, inner)
    {
    }
}