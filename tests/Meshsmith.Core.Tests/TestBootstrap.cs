using System.Runtime.CompilerServices;

internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Init() => Meshsmith.Core.NativeLoader.Configure();
}
