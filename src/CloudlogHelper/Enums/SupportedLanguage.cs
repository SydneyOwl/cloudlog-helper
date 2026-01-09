using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace CloudlogHelper.Enums;

// The order is fixed and should not be changed in future for backward compatibility.
public enum SupportedLanguage
{
    NotSpecified = -1,
    
    [Description("English")]
    English = 0,
    
    [Description("简体中文")]
    SimplifiedChinese = 1,
    
    [Description("繁體中文")]
    TraditionalChinese = 2,
    
    [Description("日本語")]
    Japanese = 3
}