namespace Prompuff.Application.Interfaces;

public interface IPlatformLauncher
{
    Task<bool> OpenFolderAsync(string path);
    Task<bool> OpenUrlAsync(Uri uri);
}
