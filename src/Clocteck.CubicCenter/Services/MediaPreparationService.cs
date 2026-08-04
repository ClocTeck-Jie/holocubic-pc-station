using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Clocteck.CubicCenter.Services;

public sealed record PreparedMedia(
    byte[] Content,
    string ContentType,
    bool Transformed,
    int Width,
    int Height);

public static class MediaPreparationService
{
    public const int DeviceWidth = 320;
    public const int DeviceHeight = 240;

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp",
    };

    public static bool CanTransform(string localPath) =>
        SupportedImageExtensions.Contains(Path.GetExtension(localPath));

    public static async Task<PreparedMedia> PrepareAsync(
        string localPath,
        string mode,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(localPath).ToLowerInvariant();
        if (!CanTransform(localPath) || mode is not ("crop" or "fit"))
        {
            return new PreparedMedia(
                await File.ReadAllBytesAsync(localPath, cancellationToken),
                ContentType(extension),
                false,
                0,
                0);
        }

        await using var input = File.OpenRead(localPath);
        using var image = await SixLabors.ImageSharp.Image.LoadAsync(input, cancellationToken);
        image.Mutate(context =>
        {
            context.AutoOrient();
            context.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(DeviceWidth, DeviceHeight),
                Mode = mode == "crop" ? ResizeMode.Crop : ResizeMode.Pad,
                Position = AnchorPositionMode.Center,
                PadColor = SixLabors.ImageSharp.Color.Black,
                Sampler = KnownResamplers.Lanczos3,
            });
        });

        await using var output = new MemoryStream();
        await image.SaveAsync(output, Encoder(extension), cancellationToken);
        return new PreparedMedia(
            output.ToArray(),
            ContentType(extension),
            true,
            image.Width,
            image.Height);
    }

    private static IImageEncoder Encoder(string extension) => extension switch
    {
        ".gif" => new GifEncoder(),
        ".jpg" or ".jpeg" => new JpegEncoder { Quality = 90 },
        ".bmp" => new BmpEncoder(),
        _ => new PngEncoder(),
    };

    private static string ContentType(string extension) => extension switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".mp3" => "audio/mpeg",
        ".lrc" or ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
