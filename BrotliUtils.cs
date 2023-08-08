using System.IO.Compression;

namespace USSR.Utilities
{
    public class BrotliUtils
    {
        static CompressionLevel GetCompressionLevel()
        {
            // NOTE: CompressionLevel.SmallestSize == 3 is not supported in .NET Core 3.1
            if (Enum.IsDefined(typeof(CompressionLevel), 3))
                return (CompressionLevel)3;

            return CompressionLevel.Optimal;
        }

        public static byte[] CompressBytes(byte[] bytes)
        {
            using MemoryStream? outputStream = new();
            using (BrotliStream? compressionStream = new(outputStream, GetCompressionLevel()))
                compressionStream.Write(bytes);

            return outputStream.ToArray();
        }

        public static string CompressFile(string originalFileName, string compressedFileName)
        {
            using FileStream originalStream = File.Open(originalFileName, FileMode.Open);
            using FileStream compressedStream = File.Create(compressedFileName);
            CompressStream(originalStream, compressedStream);

            return compressedFileName;
        }

        public static void CompressStream(Stream originalStream, Stream compressedStream)
        {
            using BrotliStream? compressor = new(compressedStream, GetCompressionLevel());
            originalStream.CopyTo(compressor);
        }

        public static byte[] DecompressBytes(byte[] bytes)
        {
            using MemoryStream? inputStream = new(bytes);
            using MemoryStream outputStream = new();
            using (BrotliStream compressionStream = new(inputStream, CompressionMode.Decompress))
                compressionStream.CopyTo(outputStream);

            return outputStream.ToArray();
        }

        public static string DecompressFile(string compressedFileName, string outputFileName)
        {
            using FileStream compressedFileStream = File.Open(compressedFileName, FileMode.Open);
            using FileStream outputFileStream = File.Create(outputFileName);
            DecompressStream(compressedFileStream, outputFileStream);

            return outputFileName;
        }

        public static void DecompressStream(Stream compressedStream, Stream outputStream)
        {
            using BrotliStream decompressor = new(compressedStream, CompressionMode.Decompress);
            decompressor.CopyTo(outputStream);
        }
    }
}

// Source: https://www.prowaretech.com/articles/current/dot-net/compression-brotli
