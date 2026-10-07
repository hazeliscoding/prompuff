using Prompuff.Application.Settings;

namespace Prompuff.Application.Interfaces;

public interface ISettingsStore
{
    /// <summary>Loads settings, falling back to defaults when the file is missing or unreadable.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
