namespace CircleQuickLauncher.Core.Validation;

public enum PngValidationError
{
    None = 0,
    Empty,
    TooLarge,
    NotPng,
    UnsupportedDimensions,
    Truncated,
}

public sealed record PngValidationResult(bool IsValid, PngValidationError Error)
{
    public static readonly PngValidationResult Ok = new(true, PngValidationError.None);

    public static PngValidationResult Fail(PngValidationError error) => new(false, error);
}

/// <summary>
/// Validates PNG icon files by parsing the file header directly, without decoding pixels.
/// This keeps untrusted files away from image decoders until they pass structural checks.
/// </summary>
public static class PngIconValidator
{
    public const int MaxFileBytes = 2 * 1024 * 1024;      // 2 MB
    public const int MaxDimension = 512;                    // pixels
    public const int MinDimension = 1;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // "IHDR" chunk type bytes.
    private static readonly byte[] IhdrType = { 0x49, 0x48, 0x44, 0x52 };

    public static PngValidationResult Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return PngValidationResult.Fail(PngValidationError.Empty);
        }

        if (data.Length > MaxFileBytes)
        {
            return PngValidationResult.Fail(PngValidationError.TooLarge);
        }

        // 8-byte signature + 4-byte length + 4-byte type + 13-byte IHDR data + 4-byte CRC.
        if (data.Length < 8 + 4 + 4 + 13 + 4)
        {
            return PngValidationResult.Fail(PngValidationError.Truncated);
        }

        if (!data[..8].SequenceEqual(PngSignature))
        {
            return PngValidationResult.Fail(PngValidationError.NotPng);
        }

        // IHDR must be the first chunk; its type lives at offset 12..16.
        if (!data.Slice(12, 4).SequenceEqual(IhdrType))
        {
            return PngValidationResult.Fail(PngValidationError.NotPng);
        }

        // Width and height are big-endian uint32 at offsets 16 and 20.
        int width = ReadBigEndianUInt32(data.Slice(16, 4));
        int height = ReadBigEndianUInt32(data.Slice(20, 4));

        if (width <= 0 || height <= 0)
        {
            return PngValidationResult.Fail(PngValidationError.UnsupportedDimensions);
        }

        if (width > MaxDimension || height > MaxDimension)
        {
            return PngValidationResult.Fail(PngValidationError.UnsupportedDimensions);
        }

        return PngValidationResult.Ok;
    }

    public static bool IsAcceptable(ReadOnlySpan<byte> data) => Validate(data).IsValid;

    private static int ReadBigEndianUInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}
