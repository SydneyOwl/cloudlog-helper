using System;
using System.Collections.Generic;
using CloudlogHelper.Enums;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.Models;

public class LogSystemConfig : ReactiveObject
{
    public string DisplayName { get; set; }

    public Type RawType { get; set; }

    public bool UploadEnabled { get; set; }

    [Reactive] public List<LogSystemField> Fields { get; set; } = new();
}

public class LogSystemField : ReactiveObject
{
    public string DisplayName { get; set; }
    public string PropertyName { get; set; }
    public FieldType Type { get; set; }
    public string? Watermark { get; set; }
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    
    /// <summary>
    ///     (Combobox only)
    /// </summary>
    public string[]? Selections { get; set; }

    [Reactive] public string? Value { get; set; }
}
