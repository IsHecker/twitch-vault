using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TwitchVault.Api.Features.Recording.Storyboard;

public interface IFrameExtractor
{
    bool ExtractFrameInto(
        string filePath, int targetSecond, int dstWidth, int dstHeight,
        Image<Bgr24> destination, int destX, int destY);

    void Reset();
}