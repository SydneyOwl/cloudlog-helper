using System;
using System.Net;

namespace CloudlogHelper.Utils;

public class IPAddrUtil
{
    /// <summary>
    ///     Parse specified address,  returns ip and port
    /// </summary>
    /// <param name="address"></param>
    /// <returns></returns>
    public static (string, int) ParseAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) throw new Exception("Invalid address format");
        var serverAddr = address.Split(":");
        if (serverAddr.Length != 2 || !int.TryParse(serverAddr[1], out var serverPort))
            throw new Exception("Invalid address format");
        if (serverPort is not (> 0 and < 65535)) throw new Exception("Invalid address format");
        var serverIp = serverAddr[0];
        if (!IPAddress.TryParse(serverIp, out _)) throw new Exception("Invalid address format");
        return (serverIp, serverPort);
    }

    public static bool CheckAddress(string address)
    {
        try
        {
            ParseAddress(address);
        }
        catch
        {
            return false;
        }

        return true;
    }
}