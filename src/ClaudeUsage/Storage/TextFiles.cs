using System.IO;
using System.Text;

namespace ClaudeUsage.Storage;

/// <summary>Shared text-file conventions for the data folder: UTF-8 without BOM, atomic replace.</summary>
public static class TextFiles
{
    /// <summary>
    /// UTF-8 without a byte-order mark, for every file the app writes. Readers use <see cref="Encoding.UTF8"/>,
    /// which still detects and skips a BOM left by older versions.
    /// </summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="text"/> to a temp file next to <paramref name="path"/> and moves it over the target,
    /// so a crash mid-write never leaves a truncated file. Callers serialise concurrent writes to the same path.
    /// </summary>
    public static void WriteAllTextAtomic(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, Utf8NoBom);
        File.Move(temp, path, overwrite: true);
    }
}
