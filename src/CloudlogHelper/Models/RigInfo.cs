using System;

namespace CloudlogHelper.Models;

/// <summary>
///     Information of the rigs. Read directly from rigctld stdout(--list)
/// </summary>
public class RigInfo
{
   public string? Id { get; set; }
   public string? Manufacturer { get; set; }
   public string? Model { get; set; }
   public string? Version { get; set; }
   public string? Status { get; set; }
   public string? Macro { get; set; }

    public override string ToString()
    {
        return
            $"{nameof(Id)}: {Id}, {nameof(Manufacturer)}: {Manufacturer}, {nameof(Model)}: {Model}, {nameof(Version)}: {Version}, {nameof(Status)}: {Status}, {nameof(Macro)}: {Macro}";
    }

    protected bool Equals(RigInfo other)
    {
        return Id == other.Id && Manufacturer == other.Manufacturer && Model == other.Model &&
               Version == other.Version && Status == other.Status && Macro == other.Macro;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RigInfo)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Manufacturer, Model, Version, Status, Macro);
    }
}