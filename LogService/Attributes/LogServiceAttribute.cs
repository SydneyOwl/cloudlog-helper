using System;

namespace CloudlogHelper.LogService.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class LogServiceAttribute : Attribute
{
    public LogServiceAttribute(string serviceName)
    {
        ServiceName = serviceName;
    }

    /// <summary>
    ///     Service name. Will be displayed on UI. e.g. QRZ.com
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    ///     Optional description of this log service.
    /// </summary>
    public string Description { get; set; } = "";
}