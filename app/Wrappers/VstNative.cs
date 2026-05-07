using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace ComposeNowPlugins.Wrappers;

internal static partial class VstNative
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

    [LibraryImport(libBase, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial IntPtr VstCreate(string pluginPath, double sampleRate, int blockSize, int channels);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void VstDestroy(IntPtr h);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void VstSetParam(IntPtr h, uint id, float norm);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void VstNoteOn(IntPtr h, int note, float vel);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void VstNoteOff(IntPtr h, int note);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void VstSetLatency(IntPtr h, uint samples);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int VstProcess(IntPtr h, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] float[] outInterleaved, int frames);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool VstReconfigure(
        IntPtr h, double sampleRate, int blockSize, int channels, int processMode /*0=rt,1=offline*/);

    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool VstGetState(
        IntPtr h,
        IntPtr buffer,          // может быть IntPtr.Zero
        ref uint size);         // in/out: размер буфера / фактический размер

    // bool VstSetState(VstHandle* h, const void* buffer, uint32_t size);
    [LibraryImport(libBase)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool VstSetState(
        IntPtr h,
        byte[] buffer,
        uint size);
}
