// Test-only shim: HtmlSource (linked into this project) calls AppUpdateService.CurrentArches
// for its auto-arch step; the real service is not linked into the test assembly.
namespace ObtainiumPC.Services.Updates
{
    public static class AppUpdateService
    {
        public static IReadOnlyList<string> CurrentArches() => new[] { "x64" };
    }
}
