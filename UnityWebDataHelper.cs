using System.Text;
using Kaitai;

namespace USSR.Utilities
{
    struct WebData
    {
        public byte[] Magic;
        public uint FirstFileOffset;
        public List<FileEntry> FileEntries;
        public List<byte[]> FileContents;
    }

    struct FileEntry
    {
        public uint FileOffset;
        public uint FileSize;
        public uint FileNameSize;
        public byte[] Name;
    }

    public class UnityWebDataHelper
    {
        const string MAGIC_HEADER = "UnityWebData1.0";

        /// <summary>
        /// Unpack UnityWebData (WebGL.data) to File.
        /// </summary>
        /// <param name="bundleFile"></param>
        /// <returns>Output directory.</returns>
        public static string UnpackWebDataToFile(string? bundleFile)
        {
            if (!File.Exists(bundleFile))
                throw new FileNotFoundException($"{bundleFile} didn\'t exist!");

            // Create the Kaitai stream and the root object from the parsed data
            UnityWebData? unityWebData = UnityWebData.FromFile(bundleFile);

            string? outputDirectory = Path.Combine(
                Path.GetDirectoryName(bundleFile),
                Path.GetFileNameWithoutExtension(bundleFile)
            );
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Console.WriteLine("Extracting bundle file...");

            foreach (UnityWebData.FileEntry fileEntry in unityWebData.Files)
            {
                string? fileName = fileEntry?.Filename;

                // Create file entry directory
                string? fileNameDirectory = Path.Combine(
                    outputDirectory,
                    Path.GetDirectoryName(fileName)
                );
                if (!Directory.Exists(fileNameDirectory))
                    Directory.CreateDirectory(fileNameDirectory);

                string? outputFile = Path.Combine(outputDirectory, fileName);

                using FileStream? outputFileStream = new(outputFile, FileMode.Create);
                outputFileStream?.Write(fileEntry?.Data);
            }

            Console.WriteLine("Extraction complete.");
            return outputDirectory;
        }

        public static void PackFilesToWebData(string sourceFolder, string outputFile)
        {
            // Get all files recursively
            List<string> files = Directory
                .GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
                .ToList();

            // Get files in root directory
            List<string> rootFolderFiles = files
                .Where(f => Path.GetDirectoryName(f) == sourceFolder)
                .ToList();

            // Get files inside subdirectories
            List<string> subdirectoryFiles = files.Except(rootFolderFiles).ToList();

            // Sort the subdirectory files in descending order
            subdirectoryFiles.Sort((a, b) => b.CompareTo(a));

            // Combine the lists and print the result
            files = subdirectoryFiles.Concat(rootFolderFiles).ToList();
            List<string>? filesName = new();

            foreach (string file in files)
                filesName.Add(
                    file.Replace(sourceFolder, "")
                        .Trim(Path.DirectorySeparatorChar)
                        .Replace(@"\", @"/")
                );

            using MemoryStream tempStream = new();
            using BinaryWriter tempWriter = new(tempStream);
            byte[] magic = AddNullTerminate(Encoding.UTF8.GetBytes(MAGIC_HEADER));
            List<FileEntry> fileEntries = new();
            List<byte[]> fileContents = new();
            List<long> fileOffsetValues = new();
            List<long> fileOffsetEntryPosition = new();

            // Collect file entries
            for (int i = 0; i < files.Count; i++)
            {
                FileInfo fileInfo = new(files[i]);
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(filesName[i]);

                fileEntries.Add(
                    new FileEntry
                    {
                        FileOffset = 0,
                        FileSize = (uint)fileInfo.Length,
                        FileNameSize = (uint)fileNameBytes.Length,
                        Name = fileNameBytes
                    }
                );

                fileContents.Add(File.ReadAllBytes(files[i]));
            }

            WebData webData =
                new()
                {
                    Magic = magic,
                    FirstFileOffset = 0,
                    FileEntries = fileEntries,
                    FileContents = fileContents
                };

            // Write Magic bytes
            tempWriter.Write(webData.Magic);

            // Write a placeholder for FirstFileOffset
            fileOffsetEntryPosition.Add(tempStream.Position);
            tempWriter.Write(webData.FirstFileOffset);

            // Write each FileEntry
            foreach (FileEntry entry in webData.FileEntries)
            {
                // Write FileOffset
                fileOffsetEntryPosition.Add(tempStream.Position);
                tempWriter.Write(entry.FileOffset);

                // Write FileSize
                tempWriter.Write(entry.FileSize);

                // Write FileNameSize
                tempWriter.Write(entry.FileNameSize);

                // Write Name bytes
                tempWriter.Write(entry.Name);
            }

            foreach (byte[] content in webData.FileContents)
            {
                // Add current offset to a list to be used later
                fileOffsetValues.Add(tempStream.Position);

                // Write the actual data
                tempWriter.Write(content);
            }

            // Go back to WebData.FirstFileOffset and write the first file offset
            tempStream.Seek(fileOffsetEntryPosition[0], SeekOrigin.Begin);
            tempWriter.Write((uint)fileOffsetValues[0]);

            // Go back to each FileEntry.FileOffset and write the file offset
            for (int i = 0; i < fileOffsetValues.Count; i++)
            {
                tempStream.Seek(fileOffsetEntryPosition[i + 1], SeekOrigin.Begin);
                tempWriter.Write((uint)fileOffsetValues[i]);
            }

            // Now write the entire contents of the temporary stream to the actual file
            using FileStream fileStream = new(outputFile, FileMode.Create);
            tempStream.WriteTo(fileStream);
        }

        /// <summary>
        /// Add null terminator at the end of bytes.
        /// </summary>
        /// <param name="originalArray"></param>
        /// <returns>New array of bytes.</returns>
        static byte[] AddNullTerminate(byte[] originalArray)
        {
            // Create a new array with one extra element to accommodate the null byte
            byte[] newArray = new byte[originalArray.Length + 1];

            // Copy the original array to the new array
            Array.Copy(originalArray, newArray, originalArray.Length);

            // Set the last element to be the null byte
            newArray[^1] = 0;

            return newArray;
        }
    }
}
