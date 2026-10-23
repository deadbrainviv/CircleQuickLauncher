using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using CircleQuickLauncher.Core.Models;
using CircleQuickLauncher.Core.Validation;
using CircleQuickLauncher.Interop;

namespace CircleQuickLauncher.Services;

/// <summary>
/// Resolves the image shown on a circle:
///   1. user-selected custom PNG (validated, copied into app data),
///   2. icon extracted from the target executable (cached as PNG),
///   3. the launcher's own logo as the universal fallback.
/// </summary>
public static class IconService
{
    public const string FallbackLogoRelativePath = "Assets/Logo.png";

    public static string CustomIconDirectory => Path.Combine(AppState.AppDataDirectory, "icons");
    public static string ExtractedIconCacheDirectory => Path.Combine(CustomIconDirectory, "cache");

    public static string DefaultLogoPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png");

    /// <summary>Returns a PNG path for the entry, or null when the launcher logo should be used.</summary>
    public static string? GetIconPngPath(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.IsNullOrEmpty(entry.CustomIconFileName))
        {
            string custom = Path.Combine(CustomIconDirectory, entry.CustomIconFileName);
            if (IsSafeIconPath(custom) && File.Exists(custom))
            {
                return custom;
            }
        }

        string? extracted = GetOrExtractCachedIcon(entry.TargetPath);
        if (extracted is not null)
        {
            return extracted;
        }

        return null; // caller falls back to the launcher logo
    }

    /// <summary>Validates and copies a user-selected PNG into the icon folder; updates the entry.</summary>
    public static void SetCustomIcon(AppEntry entry, string sourcePngPath)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var info = new FileInfo(sourcePngPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The selected icon file does not exist.", sourcePngPath);
        }

        if (info.Length > PngIconValidator.MaxFileBytes)
        {
            throw new InvalidDataException($"Icon files must be {PngIconValidator.MaxFileBytes / (1024 * 1024)} MB or smaller.");
        }

        byte[] data;
        using (var stream = File.OpenRead(sourcePngPath))
        {
            var buffer = new byte[Math.Min((int)info.Length, PngIconValidator.MaxFileBytes + 1)];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            data = buffer.AsSpan(0, read).ToArray();
        }

        PngValidationResult validation = PngIconValidator.Validate(data);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(GetValidationErrorText(validation.Error));
        }

        // Decode once with the strict platform PNG decoder before accepting it (post header checks).
        using var probe = new MemoryStream(data, writable: false);
        ValidateDecodable(probe);

        Directory.CreateDirectory(CustomIconDirectory);
        string target = Path.Combine(CustomIconDirectory, entry.Id + ".png");

        // Write via temp file for atomicity.
        string temp = target + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, target, overwrite: true);

        RemoveExtractedCache(entry.TargetPath);
        entry.CustomIconFileName = Path.GetFileName(target);
    }

    public static void ClearCustomIcon(AppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.IsNullOrEmpty(entry.CustomIconFileName))
        {
            string path = Path.Combine(CustomIconDirectory, entry.CustomIconFileName);
            if (IsSafeIconPath(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            entry.CustomIconFileName = null;
        }
    }

    public static void DeleteIconsFor(AppEntry entry)
    {
        try
        {
            ClearCustomIcon(entry);
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void ValidateDecodable(Stream stream)
    {
        try
        {
            Task<Windows.Graphics.Imaging.BitmapDecoder> task =
                Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream()).AsTask();

            if (!task.Wait(TimeSpan.FromSeconds(5)) || task.Status != TaskStatus.RanToCompletion || task.Result is null)
            {
                try
                {
                    task.Dispose();
                }
                catch (Exception)
                {
                }

                throw new InvalidDataException("The file could not be decoded as an image.");
            }

            task.Dispose();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (AggregateException ex)
        {
            throw new InvalidDataException("The file could not be decoded as an image.", ex.InnerException ?? ex);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            throw new InvalidDataException("The file could not be decoded as an image.", ex);
        }
    }

    private static string GetValidationErrorText(PngValidationError error) => error switch
    {
        PngValidationError.TooLarge => "The icon file is larger than 2 MB.",
        PngValidationError.NotPng => "The icon must be a PNG file.",
        PngValidationError.UnsupportedDimensions => "The icon must be between 1 and 512 pixels per side.",
        PngValidationError.Truncated => "The icon file appears to be incomplete.",
        PngValidationError.Empty => "The icon file is empty.",
        _ => "The icon file is not valid.",
    };

    private static string? GetOrExtractCachedIcon(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return null;
        }

        string cacheKey = Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(targetPath.ToUpperInvariant())))[..24];
        string cachePath = Path.Combine(ExtractedIconCacheDirectory, cacheKey + ".png");

        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        try
        {
            string? extracted = ExtractIconPng(targetPath, cachePath);
            return extracted;
        }
        catch (Exception ex) when (ex is System.IO.FileNotFoundException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static string? ExtractIconPng(string targetPath, string cachePath)
    {
        if (!File.Exists(targetPath))
        {
            return null;
        }

        var shfi = new NativeMethods.SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            targetPath,
            0,
            ref shfi,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

        if (shfi.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (result == IntPtr.Zero)
            {
                return null;
            }

            using var bitmap = System.Drawing.Bitmap.FromHicon(shfi.hIcon);
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return null;
            }

            Directory.CreateDirectory(ExtractedIconCacheDirectory);
            string temp = cachePath + ".tmp";
            bitmap.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
            File.Move(temp, cachePath, overwrite: true);
            return cachePath;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(shfi.hIcon);
        }
    }

    private static void RemoveExtractedCache(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        try
        {
            string cacheKey = Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(targetPath.ToUpperInvariant())))[..24];
            string cachePath = Path.Combine(ExtractedIconCacheDirectory, cacheKey + ".png");
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Guarantees a stored icon path stays inside the icon folder.</summary>
    private static bool IsSafeIconPath(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(CustomIconDirectory);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }
}
