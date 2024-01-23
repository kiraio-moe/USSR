using System.IO.Compression;

namespace USSR.Utilities
{
    internal class BrotliUtils
    {
        static CompressionLevel GetCompressionLevel()
        {
            // NOTE: CompressionLevel.SmallestSize == 3 is not supported in .NET Core 3.1
            if (Enum.IsDefined(typeof(CompressionLevel), 3))
                return (CompressionLevel)3;

            return CompressionLevel.Optimal;
        }

        internal static byte[] CompressBytes(byte[] bytes)
        {
            using MemoryStream? outputStream = new();
            using (BrotliStream? compressionStream = new(outputStream, GetCompressionLevel()))
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
            using BrotliStream? compressor = new(compressedStream, GetCompressionLevel());
            originalStream.CopyTo(compressor);
        }

        internal static byte[] DecompressBytes(byte[] bytes)
        {
            using MemoryStream? inputStream = new(bytes);
            using MemoryStream outputStream = new();
            using (BrotliStream compressionStream = new(inputStream, CompressionMode.Decompress))
                compressionStream.CopyTo(outputStream);

            return outputStream.ToArray();
        }

        internal static string DecompressFile(string compressedFileName, string outputFileName)
        {
            try
            {
                using FileStream compressedFileStream = File.Open(
                    compressedFileName,
                    FileMode.Open
                );
                using FileStream outputFileStream = File.Create(outputFileName);
                DecompressStream(compressedFileStream, outputFileStream);

                return outputFileName;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        internal static void DecompressStream(Stream compressedStream, Stream outputStream)
        {
            using BrotliStream decompressor = new(compressedStream, CompressionMode.Decompress);
            decompressor.CopyTo(outputStream);
        }

        // internal static void WriteUnityIdentifier(string filePath, byte[] magicBytes)
        // {
        //     try
        //     {
        //         using (
        //             FileStream fileStream = new FileStream(
        //                 filePath,
        //                 FileMode.Open,
        //                 FileAccess.ReadWrite
        //             )
        //         )
        //         {
        //             fileStream.Seek(0, SeekOrigin.Begin);
        //             fileStream.Write(magicBytes, 0, magicBytes.Length);
        //         }
        //     }
        //     catch { }
        // }
    }
}

// Source: https://www.prowaretech.com/articles/current/dot-net/compression-brotli
