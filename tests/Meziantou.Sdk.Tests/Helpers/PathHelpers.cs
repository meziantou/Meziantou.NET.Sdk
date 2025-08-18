using Meziantou.Framework;

namespace Meziantou.Sdk.Tests.Helpers;

internal static class PathHelpers
{
    public  static FullPath GetRootDirectory()
    {
        var directory = FullPath.CurrentDirectory();
        while (!Directory.Exists(directory / ".git"))
        {
            directory = directory.Parent;
        }

        return directory;
    }
}
