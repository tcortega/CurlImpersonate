using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CurlImpersonate.Native;

namespace CurlImpersonate;

/// <summary>
/// Module initializer that sets up the native library resolver.
/// </summary>
internal static class ModuleInitializer
{
    /// <summary>
    /// Initializes the native library resolver when the module is loaded.
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Intentional - sets up native library resolver for P/Invoke")]
    internal static void Initialize()
    {
        NativeLibraryResolver.Initialize();
    }
}
