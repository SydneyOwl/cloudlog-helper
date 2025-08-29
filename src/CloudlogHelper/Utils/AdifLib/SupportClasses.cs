using System.Collections.Generic;

// Support classes
namespace ADIFLib;

// TagNameData and TagNameDataList are used for easier building of QSO and header.
public class TokenNameData
{
    public string Data = "";
    public string TagName = "";

    public TokenNameData()
    {
    }

    public TokenNameData(string TagName, string Data)
    {
        this.TagName = TagName;
        this.Data = Data;
    }
}

public class TokenNameDataList : List<TokenNameData>
{
}

// UserDefItemHeader and UserDefItemHeaderList are used for easier building of USERDEF header items.
public class UserDefItemHeader
{
    public string Data = "";
    public string EnumerationItems = "";
    public string TagName = "";
    public char UserDefType = ' ';

    public UserDefItemHeader(string TagName, string Data, char UserDefType, string EnumerationItems)
    {
        this.TagName = TagName;
        this.Data = Data;
        this.UserDefType = UserDefType;
        this.EnumerationItems = EnumerationItems;
    }
}

public class UserDefItemHeaderList : List<UserDefItemHeader>
{
}