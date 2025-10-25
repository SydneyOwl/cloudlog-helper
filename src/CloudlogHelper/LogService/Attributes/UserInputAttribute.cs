using System;
using CloudlogHelper.Enums;

namespace CloudlogHelper.LogService.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class UserInputAttribute : Attribute
{
    public UserInputAttribute(string displayNameLangKey)
    {
        DisplayNameLangKey = displayNameLangKey;
    }

    /// <summary>
    ///     I18N Name that will be displayed on UI. e.g. Password
    ///     Will use the name of the field as display name.
    /// </summary>
    public string DisplayNameLangKey { get; set; } = "Log Service";

    /// <summary>
    ///     Will be displayed if user hover the mouse on the help button.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Is this field required?
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    ///     Should we use password mask?
    /// </summary>
    public FieldType InputType { get; set; } = FieldType.Text;

    /// <summary>
    ///     Should we use password mask?
    /// </summary>
    public string WaterMark { get; set; } = string.Empty;

    /// <summary>
    ///     (Combobox only) Should we choose something here?
    /// </summary>
    public string SelectionsArrayName { get; set; }
}