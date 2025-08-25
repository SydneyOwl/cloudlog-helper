using System.Threading.Tasks;

namespace CloudlogHelper.Services.Interfaces;

public interface IClipboardService
{
    Task<string?> GetTextAsync();

    Task SetTextAsync(string? text);

    Task ClearAsync();
}