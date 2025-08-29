using System;

namespace CloudlogHelper.Exceptions;

public class RigCommException : Exception
{
    public RigCommException(string message) : base(message)
    {
    }

    public RigCommException()
    {
    }
}