using ObtainiumPC.Services;
using Xunit;

namespace ObtainiumPC.Tests;

public class ExeIconFramePickTests
{
    private static int Pick(params (int, int)[] f) => ExeIconResource.PickFrame(f);

    [Fact]
    public void Never_picks_the_16px_frame_when_a_bigger_one_exists()
        // frame order seen in a real program: 16,24,32,48,64,256
        => Assert.Equal(4, Pick((16, 32), (24, 32), (32, 32), (48, 32), (64, 32), (256, 32)));

    [Fact]
    public void Smallest_frame_of_at_least_64_wins()
        => Assert.Equal(5, Pick((48, 32), (32, 32), (16, 32), (256, 32), (128, 32), (64, 32), (48, 8)));

    [Fact]
    public void Falls_back_to_the_largest_when_all_are_small()
        => Assert.Equal(1, Pick((16, 32), (48, 32), (32, 32)));

    [Fact]
    public void Deeper_colour_wins_a_tie()
        => Assert.Equal(1, Pick((64, 8), (64, 32)));

    [Fact]
    public void A_single_frame_is_kept()
        => Assert.Equal(0, Pick((256, 32)));
}
