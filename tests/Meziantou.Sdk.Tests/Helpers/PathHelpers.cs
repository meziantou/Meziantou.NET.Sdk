using Meziantou.Framework;

namespace Meziantou.Sdk.Tests.Helpers;

internal static class PathHelpers
{
    public  static FullPath GetRootDirectory()
    {
        return FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
    }
}
