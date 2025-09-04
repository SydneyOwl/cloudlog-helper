using System.Linq;
using System.Text.RegularExpressions;

namespace CloudlogHelper.Utils;

public class WsjtxMessageUtil
{
    public static string? ExtractGridFromMessage(string message)
    {
        var messageInfo = message.Trim().Split(" ");
        if (messageInfo.Length < 3) return null;
        
        var grid = messageInfo[^1].Trim();

        return MaidenheadGridUtil.CheckMaidenhead(grid) ? grid : null;
    }
    
    public static string? ExtractDeFromMessage(string message)
    {
        var messageInfo = message.Trim().Split(" ");
        if (messageInfo.Length < 3) return null;
        
        var de = messageInfo[^2].Trim();
        
        if (string.IsNullOrWhiteSpace(de))
            return null;

        if (de.Any(char.IsLetter) && de.Any(char.IsDigit))
        {
            return de;
        }
        return null;
    }
    
    
}