using System;

namespace CloudlogHelper.Exceptions;

public class InvalidConfigurationException : ArgumentException
{
    public InvalidConfigurationException()
    {
    }

    public InvalidConfigurationException(string message) : base(message)
    {
    }

    public InvalidConfigurationException(string message, Exception inner) : base(message, inner)
    {
    }
}