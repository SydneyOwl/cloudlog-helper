using Avalonia.Media;

namespace CloudlogHelper.Services.Interfaces;

public interface ICountryService
{
    IImage GetFlagResourceByDXCC(string? dxcc);
}