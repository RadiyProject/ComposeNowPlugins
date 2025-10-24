using System.Reflection;
namespace ComposeNowPlugins.Wrappers;

using System.Runtime.InteropServices;

internal static class VstNative
{
    private const string libBase = "VstHostCapi";

    static VstNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(VstNative).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly asm, DllImportSearchPath? _)
    {
        if (!string.Equals(libraryName, libBase, StringComparison.Ordinal))
            return IntPtr.Zero;

        // 1) Берём базу из ENV/конфига (например: /plugins/SineSynthHeadless/lib/Release)
        var baseDir = "/plugins/" + (
            Environment.GetEnvironmentVariable("VST_HOST_LIB_DIR")
            ?? "SineSynthHeadless") + "/lib/Release";

        // 2) Подбираем имя под ОС
        var fileName = $"lib{libBase}.so";

        var fullPath = Path.Combine(baseDir, fileName);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException(fullPath);

        if (!NativeLibrary.TryLoad(fullPath, out var handle))
            throw new DllNotFoundException($"Failed to load {fullPath}");

        try { NativeLibrary.GetExport(handle, "VstCreate"); }
        catch (Exception e)
        {
            throw new EntryPointNotFoundException($"Symbol VstCreate not found in {fullPath}", e);
        }
        return handle;
    }

    [DllImport(libBase, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern IntPtr VstCreate(string pluginPath, double sampleRate, int blockSize, int channels);

    [DllImport(libBase, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern void VstDestroy(IntPtr h);

    [DllImport(libBase, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern void VstSetParam(IntPtr h, uint id, float norm);

    [DllImport(libBase, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern void VstNoteOn(IntPtr h, int note, float vel);

    [DllImport(libBase, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern void VstNoteOff(IntPtr h, int note);

    [DllImport(libBase, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern void VstSetLatency(IntPtr h, uint samples);

    [DllImport(libBase, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int VstProcess(IntPtr h, float[] outInterleaved, int frames);
}
