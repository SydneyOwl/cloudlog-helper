using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace CloudlogHelper.Utils;

public class SerialUtil
{
    /// <summary>
    ///     Automatically select the appropriate serial port on windows.
    ///     NOT Compatible on win7.
    /// </summary>
    /// <returns></returns>
    private static string WinSelectSerialByName()
    {
        var comList = WinGetSerialDevices();
        var ccName = "";
        for (var i = 0; i < comList.Count; i++)
        {
            var cachedName = comList[i].Properties["Name"].Value.ToString();
            if (cachedName.Contains("USB-SERIAL") || cachedName.Contains("CH340") || cachedName.Contains("PL2303") ||
                cachedName.Contains("FT232"))
            {
                if (ccName != "") return "";
                ccName = cachedName.Split("(").Last().Split(")")[0];
            }
        }

        return ccName;
    }

    /// <summary>
    ///     Automatically select the appropriate serial port on linux.
    /// </summary>
    /// <returns></returns>
    private static string LinuxSelectSerialByName()
    {
        string[] portNames = SerialPort.GetPortNames();
        var ccName = "";
        for (var i = 0; i < portNames.Length; i++)
            if (portNames[i].Contains("/dev/ttyUSB"))
            {
                if (ccName != "") return "";
                ccName = portNames[i];
            }

        return ccName;
    }

    /// <summary>
    ///     Automatically select the appropriate serial port on OSX.
    /// </summary>
    /// <returns></returns>
    private static string MacOsSelectSerialByName()
    {
        string[] portNames = SerialPort.GetPortNames();
        var ccName = "";
        for (var i = 0; i < portNames.Length; i++)
            if (portNames[i].Contains("/dev/cu.usbserial"))
            {
                if (ccName != "") return "";
                ccName = portNames[i];
            }

        return ccName;
    }

    /// <summary>
    ///     Automatically select the appropriate serial port
    /// </summary>
    /// <returns></returns>
    public static string PreSelectSerialByName()
    {
        if (OperatingSystem.IsWindows()) return WinSelectSerialByName();
        if (OperatingSystem.IsLinux()) return LinuxSelectSerialByName();
        if (OperatingSystem.IsMacOS()) return MacOsSelectSerialByName();
        return "";
    }

    /// <summary>
    ///     Get all serial devices on windows.
    /// </summary>
    /// <returns></returns>
    private static List<ManagementBaseObject> WinGetSerialDevices()
    {
        List<ManagementBaseObject> list = new();
        if (!OperatingSystem.IsWindows()) return new List<ManagementBaseObject>();
        using (var searcher = new ManagementObjectSearcher
                   ("select * from Win32_PnPEntity where Name like '%(COM%'"))
        {
            var hardInfos = searcher.Get();
            foreach (var hardInfo in hardInfos)
                if (hardInfo.Properties["Name"].Value != null)
                    list.Add(hardInfo);
        }

        return list;
    }
}