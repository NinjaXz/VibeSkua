using System.IO.Compression;
using System.Text;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Reads the frame labels of a map SWF.
///
/// AQW places NPCs by frame range: a placement function says "frames 12 to 30"
/// and the timeline label on frame 12 is the cell name. Without the labels the
/// placement blocks say which NPCs exist but not where they stand, so this tiny
/// tag walk is what turns a list of NPCs into directions.
///
/// Only the header, FrameLabel (43), ShowFrame (1) and End (0) are read. A map
/// whose SWF cannot be parsed returns no labels: the NPC list is still useful
/// without cells.
/// </summary>
internal static class SwfFrameLabels
{
    public static List<(int Frame, string Name)> Read(string swfPath)
    {
        try
        {
            return Parse(File.ReadAllBytes(swfPath));
        }
        catch
        {
            return new List<(int, string)>();
        }
    }

    internal static List<(int Frame, string Name)> Parse(byte[] file)
    {
        List<(int, string)> labels = new();
        if (file.Length < 8)
            return labels;

        byte[] body;
        if (file[0] == 'C' && file[1] == 'W' && file[2] == 'S')
        {
            // CWS: the byte length in the header, then a zlib stream.
            using MemoryStream input = new(file, 8, file.Length - 8);
            using ZLibStream zlib = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            zlib.CopyTo(output);
            body = output.ToArray();
        }
        else if (file[0] == 'F' && file[1] == 'W' && file[2] == 'S')
        {
            body = file[8..];
        }
        else
        {
            return labels;
        }

        if (body.Length < 8)
            return labels;

        int nbits = body[0] >> 3;
        if (nbits <= 0 || nbits > 64)
            return labels;

        // Header: one flags byte, four rectangles of nbits each, frame rate
        // (FIXED16) and frame count (UI16).
        int pos = (5 + 4 * nbits + 7) / 8 + 4;
        int frame = 1;

        while (pos + 2 <= body.Length)
        {
            int header = body[pos] | (body[pos + 1] << 8);
            pos += 2;
            int code = header >> 6;
            int length = header & 0x3F;
            if (length == 0x3F)
            {
                if (pos + 4 > body.Length)
                    break;
                length = body[pos] | (body[pos + 1] << 8) | (body[pos + 2] << 16) | (body[pos + 3] << 24);
                pos += 4;
            }

            if (length < 0 || pos + length > body.Length)
                break;

            if (code == 43 && length > 0)
            {
                int end = Array.IndexOf(body, (byte)0, pos, length);
                string name = Encoding.UTF8.GetString(body, pos, (end < 0 ? pos + length : end) - pos);
                if (name.Length > 0)
                    labels.Add((frame, name));
            }
            else if (code == 1)
            {
                frame++;
            }
            else if (code == 0)
            {
                break;
            }

            pos += length;
        }

        return labels;
    }
}
