using System.IO;
using System.Text;

namespace PolyView.Export;

/// <summary>
/// Assimp's FBX writer always declares centimetres (UnitScaleFactor = 1) while PolyView works in metres, so Blender
/// would import everything 100x too small. Rewrite the GlobalSettings doubles in place (same byte length, no offsets move).
/// </summary>
static class FbxUnits
{
    public static void PatchToMetres(string path)
    {
        var data = File.ReadAllBytes(path);
        bool changed = PatchDouble(data, "UnitScaleFactor", 100.0) | PatchDouble(data, "OriginalUnitScaleFactor", 100.0);
        if (changed)
        {
            File.WriteAllBytes(path, data);
        }
    }

    /// <summary>
    /// Binary FBX P70 record: S"name" S"double" S"Number" S"" D value. Find the name and overwrite the following D value.
    /// ASCII FBX: P: "name", "double", "Number", "",value.
    /// </summary>
    static bool PatchDouble(byte[] data, string name, double value)
    {
        var key = Encoding.ASCII.GetBytes(name);
        bool changed = false;
        for (int i = 0; i + key.Length < data.Length; i++)
        {
            if (!data.AsSpan(i, key.Length).SequenceEqual(key))
            {
                continue;
            }

            // Binary: the name is preceded by 'S' + uint32 length.
            if (i < 5 || data[i - 5] != (byte)'S' || BitConverter.ToUInt32(data, i - 4) != key.Length)
            {
                continue;
            }

            int p = i + key.Length;
            for (int s = 0; s < 3 && p + 5 <= data.Length; s++)
            {
                if (data[p] != (byte)'S')
                {
                    p = -1;
                    break;
                }

                p += 5 + (int)BitConverter.ToUInt32(data, p + 1);
            }

            if (p < 0 || p + 9 > data.Length || data[p] != (byte)'D')
            {
                continue;
            }

            BitConverter.GetBytes(value).CopyTo(data, p + 1);
            changed = true;
        }

        return changed;
    }
}
