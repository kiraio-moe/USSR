using System.IO.Compression;

namespace USSR.Utilities
{
    internal class GZipUtils
    {
        static CompressionLevel GetCompressionLevel()
        {
            // NOTE: CompressionLevel.SmallestSize == 3 is not supported in .NET Core 3.1 but is in .NET 6
            if (Enum.IsDefined(typeof(CompressionLevel), 3))
                return (CompressionLevel)3;

            return CompressionLevel.Optimal;
        }

        internal static byte[] CompressBytes(byte[] bytes)
        {
            using MemoryStream? outputStream = new();
            using (GZipStream? compressionStream = new(outputStream, GetCompressionLevel()))
                compressionStream.Write(bytes);

            return outputStream.ToArray();
        }

        internal static string CompressFile(string originalFileName, string compressedFileName)
        {
            using FileStream originalStream = File.Open(originalFileName, FileMode.Open);
            using FileStream compressedStream = File.Create(compressedFileName);
            CompressStream(originalStream, compressedStream);

            return compressedFileName;
        }

        internal static void CompressStream(Stream originalStream, Stream compressedStream)
        {
            using GZipStream? compressor = new(compressedStream, GetCompressionLevel());
            originalStream.CopyTo(compressor);
        }

        internal static byte[] DecompressBytes(byte[] bytes)
        {
            using MemoryStream? inputStream = new(bytes);
            using MemoryStream? outputStream = new();
            using (GZipStream? compressionStream = new(inputStream, CompressionMode.Decompress))
                compressionStream.CopyTo(outputStream);

            return outputStream.ToArray();
        }

        internal static string DecompressFile(string compressedFileName, string outputFileName)
        {
            using FileStream compressedFileStream = File.Open(compressedFileName, FileMode.Open);
            using FileStream outputFileStream = File.Create(outputFileName);
            DecompressStream(compressedFileStream, outputFileStream);

            return outputFileName;
        }

        internal static void DecompressStream(Stream compressedStream, Stream outputStream)
        {
            using GZipStream? decompressor = new(compressedStream, CompressionMode.Decompress);
            decompressor.CopyTo(outputStream);
        }
    }
}

// Source: https://www.prowaretech.com/articles/current/dot-net/compression-gzip
