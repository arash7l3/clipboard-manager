using System.Runtime.InteropServices;

namespace Clipo;

/// <summary>Whether Clipo runs from an MSIX package, which changes how it starts at sign-in.</summary>
internal static partial class PackageIdentity
{
    /// <summary>What <c>GetCurrentPackageFullName</c> returns for a process without a package.</summary>
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, 0) != AppModelErrorNoPackage;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);
}
