using CircleQuickLauncher.Core.Validation;
using Xunit;

namespace CircleQuickLauncher.Tests;

public class PngIconValidatorTests
{
    private static byte[] BuildPngHeader(int width, int height)
    {
        // Signature (8) + IHDR length (4) + "IHDR" (4) + data (13: w,h,bitdepth,ctype,comp,filter,interlace) + CRC (4).
        var data = new byte[33];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        signature.CopyTo(data, 0);
        data[11] = 13; // IHDR chunk length (big endian, high bytes are zero)
        data[12] = (byte)'I';
        data[13] = (byte)'H';
        data[14] = (byte)'D';
        data[15] = (byte)'R';
        data[16] = (byte)(width >> 24);
        data[17] = (byte)(width >> 16);
        data[18] = (byte)(width >> 8);
        data[19] = (byte)width;
        data[20] = (byte)(height >> 24);
        data[21] = (byte)(height >> 16);
        data[22] = (byte)(height >> 8);
        data[23] = (byte)height;
        data[24] = 8;  // bit depth
        data[25] = 6;  // truecolor+alpha
        data[26] = 0;  // compression
        data[27] = 0;  // filter
        data[28] = 0;  // interlace
        return data;
    }

    [Fact]
    public void Validate_ValidHeader_IsAccepted()
    {
        byte[] png = BuildPngHeader(64, 64);
        PngValidationResult result = PngIconValidator.Validate(png);

        Assert.True(result.IsValid);
        Assert.Equal(PngValidationError.None, result.Error);
    }

    [Fact]
    public void Validate_Empty_IsRejected()
    {
        PngValidationResult result = PngIconValidator.Validate(ReadOnlySpan<byte>.Empty);

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationError.Empty, result.Error);
    }

    [Fact]
    public void Validate_WrongMagic_IsRejected()
    {
        byte[] fake = BuildPngHeader(64, 64);
        fake[1] = (byte)'E'; // break PNG signature

        PngValidationResult result = PngIconValidator.Validate(fake);

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationError.NotPng, result.Error);
    }

    [Fact]
    public void Validate_JpegMagic_IsRejected()
    {
        byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        PngValidationResult result = PngIconValidator.Validate(jpeg);

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationError.NotPng, result.Error);
    }

    [Fact]
    public void Validate_TruncatedFile_IsRejected()
    {
        byte[] png = BuildPngHeader(64, 64);
        var truncated = png.AsSpan(0, 20).ToArray();

        PngValidationResult result = PngIconValidator.Validate(truncated);

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationError.Truncated, result.Error);
    }

    [Theory]
    [InlineData(513, 64)]
    [InlineData(64, 513)]
    [InlineData(10000, 10000)]
    public void Validate_OversizedDimensions_IsRejected(int width, int height)
    {
        byte[] png = BuildPngHeader(width, height);

        PngValidationResult result = PngIconValidator.Validate(png);

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationError.UnsupportedDimensions, result.Error);
    }

    [Fact]
    public void Validate_ZeroDimension_IsRejected()
    {
        byte[] png = BuildPngHeader(0, 64);

        PngValidationResult result = PngIconValidator.Validate(png);

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationError.UnsupportedDimensions, result.Error);
    }

    [Fact]
    public void Validate_FileLargerThanLimit_IsRejected()
    {
        var huge = new byte[PngIconValidator.MaxFileBytes + 1];

        PngValidationResult result = PngIconValidator.Validate(huge);

        Assert.False(result.IsValid);
        Assert.Equal(PngValidationError.TooLarge, result.Error);
    }

    [Fact]
    public void Validate_MaxAllowedDimensions_AreAccepted()
    {
        byte[] png = BuildPngHeader(PngIconValidator.MaxDimension, PngIconValidator.MaxDimension);

        Assert.True(PngIconValidator.Validate(png).IsValid);
    }
}
