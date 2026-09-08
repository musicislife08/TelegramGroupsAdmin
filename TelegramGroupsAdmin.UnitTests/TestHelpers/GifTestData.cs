namespace TelegramGroupsAdmin.UnitTests.TestHelpers;

/// <summary>
/// Hand-built GIF fixtures. Generating these by hand keeps the test suite free of
/// an imaging library that can encode animated GIFs.
/// </summary>
public static class GifTestData
{
    /// <summary>
    /// Minimal 2-frame GIF89a (4x4, frame 0 red, frame 1 blue). Uses the
    /// clear-code-before-every-pixel LZW form so the code width never grows past
    /// 3 bits, which needs no table bookkeeping to be valid.
    /// </summary>
    public static byte[] TwoFrameRedThenBlue()
    {
        const int W = 4, H = 4;
        var g = new List<byte>();

        g.AddRange("GIF89a"u8.ToArray());
        g.AddRange([W, 0, H, 0]);
        g.Add(0xF0);                        // global colour table, 2 entries
        g.AddRange([0, 0]);
        g.AddRange([0xFF, 0x00, 0x00]);     // index 0: red
        g.AddRange([0x00, 0x00, 0xFF]);     // index 1: blue

        g.AddRange([0x21, 0xFF, 0x0B]);
        g.AddRange("NETSCAPE2.0"u8.ToArray());
        g.AddRange([0x03, 0x01, 0x00, 0x00, 0x00]);

        foreach (var colourIndex in (byte[])[0, 1])
        {
            g.AddRange([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]);
            g.AddRange([0x2C, 0, 0, 0, 0, W, 0, H, 0, 0x00]);

            g.Add(0x02);                    // LZW minimum code size
            var codes = new List<int>();
            for (var i = 0; i < W * H; i++)
            {
                codes.Add(4);               // clear
                codes.Add(colourIndex);
            }
            codes.Add(5);                   // end of information

            var bytes = new List<byte>();
            var accumulator = 0;
            var bitsHeld = 0;
            foreach (var code in codes)
            {
                accumulator |= code << bitsHeld;
                bitsHeld += 3;
                while (bitsHeld >= 8)
                {
                    bytes.Add((byte)(accumulator & 0xFF));
                    accumulator >>= 8;
                    bitsHeld -= 8;
                }
            }
            if (bitsHeld > 0) bytes.Add((byte)(accumulator & 0xFF));

            g.Add((byte)bytes.Count);
            g.AddRange(bytes);
            g.Add(0x00);
        }

        g.Add(0x3B);
        return g.ToArray();
    }
}
