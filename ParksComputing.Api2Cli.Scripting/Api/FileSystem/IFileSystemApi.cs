using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.ClearScript;

namespace ParksComputing.Api2Cli.Scripting.Api.FileSystem;
public interface IFileSystemApi {
    /// <summary>
    /// Checks whether the specified file exists.
    /// </summary>
    /// <param name="path">The full or relative path to the file.</param>
    /// <returns><c>true</c> if the file exists; otherwise <c>false</c>.</returns>
    [ScriptMember("exists")]
    bool Exists(string path);

    /// <summary>
    /// Reads the entire contents of a file as a UTF-8 string.
    /// </summary>
    /// <param name="path">The full or relative path to the file.</param>
    /// <returns>The file contents as a string.</returns>
    /// <exception cref="ArgumentException">Thrown if path is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="IOException">Thrown for I/O errors or permission issues.</exception>
    [ScriptMember("readText")]
    string ReadText(string path);
}
