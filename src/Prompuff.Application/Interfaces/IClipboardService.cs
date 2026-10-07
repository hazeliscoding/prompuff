namespace Prompuff.Application.Interfaces;

public interface IClipboardService
{
    Task SetTextAsync(string text);

    /// <summary>Returns the clipboard text, or null when the clipboard holds no text.</summary>
    Task<string?> GetTextAsync();
}
