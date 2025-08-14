using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.IO;

namespace ParksComputing.Api2Cli.Scripting.Api.FileSystem.Impl;

public class FileSystemApi : IFileSystemApi {
    public bool Exists(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        try {
            string fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath);
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// Reads the entire contents of a file as a UTF-8 string.
    /// </summary>
    /// <param name="path">The full or relative path to the file.</param>
    /// <returns>The file contents as a string.</returns>
    /// <exception cref="ArgumentException">Thrown if path is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="IOException">Thrown for I/O errors or permission issues.</exception>
    public string ReadText(string path) {
        if (string.IsNullOrWhiteSpace(path)){
            throw new ArgumentException("Path must not be null or empty.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath)){
            throw new FileNotFoundException($"The file '{fullPath}' does not exist.", fullPath);
        }

        try {
            return File.ReadAllText(fullPath, UTF8Encoding.UTF8);
        }
        catch (UnauthorizedAccessException ex) {
            throw new IOException($"Access to file '{fullPath}' is denied.", ex);
        }
        catch (IOException ex) {
            throw new IOException($"Error reading file '{fullPath}': {ex.Message}", ex);
        }
    }
}
