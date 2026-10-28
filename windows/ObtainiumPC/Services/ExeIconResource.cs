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

    /// Index of the frame to use: the smallest one that is at least 64 px wide (sharp from 100% to 200%
    /// display scale without a huge downscale), else the largest; the deeper colour depth wins ties.
    public static int PickFrame(IReadOnlyList<(int Width, int BitCount)> frames)
    {
        var best = -1;
        for (var i = 0; i < frames.Count; i++)
        {
            if (best < 0) { best = i; continue; }
            var (w, bits) = frames[i];
            var (bw, bbits) = frames[best];
            bool cand = w >= 64, curr = bw >= 64;
            bool better =
                cand && !curr ? true :
                !cand && curr ? false :
                cand ? (w < bw || (w == bw && bits > bbits))
                     : (w > bw || (w == bw && bits > bbits));
            if (better) best = i;
        }
        return best < 0 ? 0 : best;
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
            // The decoder picked a small frame (blocky icon, run 40606693662: reordering alone left pixels identical) and an
            // 8x downscale from 256 is aliased too. Hand it one frame: the smallest >= 64 px (sharp 100% to 200%), else the largest.
            var pick = PickFrame(entries.Select(e => ((int)(e.hdr[0] == 0 ? 256 : e.hdr[0]), (int)BitConverter.ToUInt16(e.hdr, 6))).ToList());
            entries = new List<(byte[] hdr, byte[] img)> { entries[pick] };
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
