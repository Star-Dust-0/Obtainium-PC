using ObtainiumPC.Services;
using Xunit;

namespace ObtainiumPC.Tests;

/// The icon extracted from an exe must start with its largest frame: the image decoder shows the first frame, and a
/// small first frame is stretched into the 32-48 DIP slot (soft, pixelated app-list icons).
public class ExeIconResourceTests
{
    [Fact]
    public void Extracted_ico_lists_largest_frame_first()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[] { Path.Combine(win, "explorer.exe"), Path.Combine(sys, "mmc.exe"), Path.Combine(sys, "cmd.exe"), Path.Combine(sys, "msinfo32.exe"), Path.Combine(sys, "taskmgr.exe") };
        var checkedAny = false;
        foreach (var exe in candidates.Where(File.Exists))
        {
            var ico = ExeIconResource.TryBuildIco(exe);
            if (ico == null) continue;
            int count = BitConverter.ToUInt16(ico, 4);
            var widths = Enumerable.Range(0, count).Select(i => ico[6 + i * 16] == 0 ? 256 : (int)ico[6 + i * 16]).ToList();
            Assert.Equal(widths.OrderByDescending(w => w).ToList(), widths);
            checkedAny = true;
        }
        Assert.True(checkedAny, "no system exe with an icon resource was found to check");
    }
}
