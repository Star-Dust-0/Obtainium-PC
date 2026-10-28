using System.Runtime.InteropServices;

namespace ObtainiumPC.Services;

/// Builds an in-memory .ico from a PE file's first RT_GROUP_ICON resource (the program's own
/// icon), without the shell. Returns null when the file has no usable icon resource.
internal static class ExeIconResource
{
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
    private static readonly IntPtr RT_ICON = (IntPtr)3;
    private static readonly IntPtr RT_GROUP_ICON = (IntPtr)14;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string file, IntPtr hFile, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr h, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadResource(IntPtr h, IntPtr res);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LockResource(IntPtr res);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SizeofResource(IntPtr h, IntPtr res);
    private delegate bool EnumNameProc(IntPtr h, IntPtr type, IntPtr name, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNamesW(IntPtr h, IntPtr type, EnumNameProc proc, IntPtr param);

    private static byte[]? Read(IntPtr h, IntPtr type, IntPtr name)
    {
        var res = FindResourceW(h, name, type);
        if (res == IntPtr.Zero) return null;
        var size = (int)SizeofResource(h, res);
        var data = LoadResource(h, res);
        if (size <= 0 || data == IntPtr.Zero) return null;
        var ptr = LockResource(data);
        if (ptr == IntPtr.Zero) return null;
        var buf = new byte[size];
        Marshal.Copy(ptr, buf, 0, size);
        return buf;
    }

    public static byte[]? TryBuildIco(string path)
    {
        var h = IntPtr.Zero;
        try
        {
            h = LoadLibraryExW(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (h == IntPtr.Zero) return null;
            // First group icon by resource order (what Explorer shows is the lowest id/first name).
            IntPtr first = IntPtr.Zero;
            EnumResourceNamesW(h, RT_GROUP_ICON, (_, _, name, _) => { first = name; return false; }, IntPtr.Zero);
            if (first == IntPtr.Zero) return null;
            var grp = Read(h, RT_GROUP_ICON, first);
            if (grp == null || grp.Length < 6) return null;
            int count = BitConverter.ToUInt16(grp, 4);
            var entries = new List<(byte[] hdr, byte[] img)>();
            for (int i = 0; i < count; i++)
            {
                int o = 6 + i * 14;
                if (o + 14 > grp.Length) break;
                int id = BitConverter.ToUInt16(grp, o + 12);
                var img = Read(h, RT_ICON, (IntPtr)id);
                if (img == null) continue;
                var hdr = new byte[16];
                Array.Copy(grp, o, hdr, 0, 12); // width,height,colors,reserved,planes,bitcount,bytesInRes
                BitConverter.GetBytes((uint)img.Length).CopyTo(hdr, 8);
                entries.Add((hdr, img));
            }
            if (entries.Count == 0) return null;
            // The decoder shows the first frame. Exe groups are often authored smallest-first, which made a 16px frame get
            // stretched to the 32-48 DIP list/detail slot (the soft, pixelated look). Largest frame first; width 0 means 256.
            entries = entries.OrderByDescending(e => e.hdr[0] == 0 ? 256 : e.hdr[0]).ThenByDescending(e => BitConverter.ToUInt16(e.hdr, 6)).ToList();
            // Evidence (ui-smoke 31b, Notepad++): ordering alone did not change the pixels, the decoder still picked a ~32px
            // frame for a 32 DIP slot (1:1, blocky). Hand it only the largest frame so it always scales down from it.
            entries = entries.Take(1).ToList();
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)entries.Count);
            int offset = 6 + 16 * entries.Count;
            foreach (var (hdr, img) in entries)
            {
                BitConverter.GetBytes((uint)offset).CopyTo(hdr, 12);
                bw.Write(hdr);
                offset += img.Length;
            }
            foreach (var (_, img) in entries) bw.Write(img);
            bw.Flush();
            return ms.ToArray();
        }
        catch { return null; }
        finally { if (h != IntPtr.Zero) FreeLibrary(h); }
    }
}
