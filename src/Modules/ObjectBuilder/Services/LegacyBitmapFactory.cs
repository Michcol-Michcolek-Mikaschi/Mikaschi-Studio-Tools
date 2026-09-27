using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Modules.ObjectBuilder.Services;

public static class LegacyBitmapFactory
{
    public static Bitmap FromBgra(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var frameBuffer = bitmap.Lock();
        for (var row = 0; row < height; row++)
        {
            var destination = IntPtr.Add(frameBuffer.Address, row * frameBuffer.RowBytes);
            Marshal.Copy(pixels, row * width * 4, destination, width * 4);
        }

        return bitmap;
    }
}
