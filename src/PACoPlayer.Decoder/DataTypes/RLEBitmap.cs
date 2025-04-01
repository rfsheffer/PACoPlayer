// Copyright (c) GregDom LLC. All Rights Reserved.
// This file is licensed for use under the MIT license.

using PACoPlayer.Decoder.Records;


namespace PACoPlayer.Decoder.DataTypes
{
    public struct RawColorBytes : IEquatable<RawColorBytes>
    {
        public readonly byte R { get; }
        public readonly byte G { get; }
        public readonly byte B { get; }

        public RawColorBytes(byte r, byte g, byte b)
        {
            (R, G, B) = (r, g, b);
        }

        public RawColorBytes(int hex)
        {
            R = (byte)(hex >> 16);
            G = (byte)((hex >> 8) & 0x0000FF);
            B = (byte)(hex & 0x0000FF);
        }

        public override readonly bool Equals(object obj)
        {
            if (obj is not RawColorBytes)
            {
                return false;
            }
            RawColorBytes otherCol = (RawColorBytes)obj;
            return R == otherCol.R && G == otherCol.G && B == otherCol.B;
        }

        public override int GetHashCode() => R.GetHashCode() ^ G.GetHashCode() ^ B.GetHashCode();

        public static bool operator ==(RawColorBytes left, RawColorBytes right) => left.Equals(right);

        public static bool operator !=(RawColorBytes left, RawColorBytes right) => !(left == right);

        public bool Equals(RawColorBytes other) => R == other.R && G == other.G && B == other.B;
    }

    public class RLEException : Exception
    {
        public RLEException(string message) : base(message)
        {
        }

        public RLEException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public RLEException()
        {
        }
    }

    public class RLEBitmap
    {
        private readonly int _width;
        private readonly int _height;
        private readonly Color _solidColor;
        private readonly byte[] _imageBytes;

        public RLEBitmap(int width, int height, byte[] bytes, Color solidColor)
        {
            _width = width;
            _height = height;
            _imageBytes = new byte[width * height * 4];
            _solidColor = solidColor;

            if (bytes.Length > 0)
            {
                DecodeRLE(bytes);
            }
        }

        public void SetPixel(int x, int y, RawColorBytes color)
        {
            int offset = (((_height - y - 1) * _width) + x) * 4;
            _imageBytes[offset + 0] = color.B;
            _imageBytes[offset + 1] = color.G;
            _imageBytes[offset + 2] = color.R;
        }

        public byte[] GetBitmapBytes()
        {
            const int imageHeaderSize = 54;
            byte[] bmpBytes = new byte[_imageBytes.Length + imageHeaderSize];
            bmpBytes[0] = (byte)'B';
            bmpBytes[1] = (byte)'M';
            bmpBytes[14] = 40;
            Array.Copy(BitConverter.GetBytes(bmpBytes.Length), 0, bmpBytes, 2, 4);
            Array.Copy(BitConverter.GetBytes(imageHeaderSize), 0, bmpBytes, 10, 4);
            Array.Copy(BitConverter.GetBytes(_width), 0, bmpBytes, 18, 4);
            Array.Copy(BitConverter.GetBytes(_height), 0, bmpBytes, 22, 4);
            Array.Copy(BitConverter.GetBytes(32), 0, bmpBytes, 28, 2);
            Array.Copy(BitConverter.GetBytes(_imageBytes.Length), 0, bmpBytes, 34, 4);
            Array.Copy(_imageBytes, 0, bmpBytes, imageHeaderSize, _imageBytes.Length);
            return bmpBytes;
        }

        // NOTE: Decoding info comes from "CAVF RLE notes.pdf" which contains two documents contradicting eachother.
// TODO WHEN DONE: Remove all these disables
#pragma warning disable CA1822 // Mark members as static
#pragma warning disable IDE0059 // Unnecessary assignment of a value
#pragma warning disable CS0162 // Unreachable code detected
        private void DecodeRLE(byte[] bytes)
        {
            /*
             * CURRENT ASSUMPTIONS:
             * WORD = 16bits (platforms around 1990)
             * SCAN LINE = bitmap row
             * PIXEL = single decoded entry
             * DECODED BYTE = pixel value which is a lookup into the 256 color look up table (LUT)
             * ab cd ef gh ij etc denote bytes and nibbles in order
             */
            List<byte> bytesDecoded = new List<byte>();

            // LUT table
            List<byte> lastTableBytes = new List<byte>();

            int byteIndex;
            for (byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
            {
                sbyte b = (sbyte)bytes[byteIndex]; // descriptor byte
                if (b > 0)
                {
                    // N bytes of uncompressed data follow
                    int count = b;
                    for (int i = 0; i < count; ++i)
                    {
                        byteIndex += 1;
                        bytesDecoded.Add(bytes[byteIndex]);
                    }
                }
                else if (b == 0)
                {
                    //               bytes: 1  2  3  4  5  6  7
                    // generic escape code: 0  ab cd ef gh ij kl
                    byteIndex += 1;
                    byte code = (byte)(bytes[byteIndex] >> 4);
                    switch (code)
                    {
                        case 0:
                            {
                                // bcd is count for uncompressed data
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 1;
                                for (int i = 0; i < bcd; ++i)
                                {
                                    byteIndex += 1;
                                    bytesDecoded.Add(bytes[byteIndex]);
                                }
                                break;
                            }
                        case 1:
                            {
                                // bcd is count for byte-data ef
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 1;
                                byte ef = bytes[byteIndex += 1];
                                for (int i = 0; i < bcd; ++i)
                                {
                                    bytesDecoded.Add(ef);
                                }
                                break;
                            }
                        case 2:
                            {
                                // bcd is count for word-data efgh
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 1;
                                byte ef = bytes[byteIndex += 1];
                                byte gh = bytes[byteIndex += 1];
                                for (int i = 0; i < bcd; ++i)
                                {
                                    bytesDecoded.Add(ef);
                                    bytesDecoded.Add(gh);
                                }
                                break;
                            }
                        case 3:
                            {
                                // bcd is count for long-data efghijkl
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 1;
                                byte ef = bytes[byteIndex += 1];
                                byte gh = bytes[byteIndex += 1];
                                byte ij = bytes[byteIndex += 1];
                                byte kl = bytes[byteIndex += 1];
                                for (int i = 0; i < bcd; ++i)
                                {
                                    bytesDecoded.Add(ef);
                                    bytesDecoded.Add(gh);
                                    bytesDecoded.Add(ij);
                                    bytesDecoded.Add(kl);
                                }
                                break;
                            }
                        case 4:
                            {
                                // bcd is count of the number of pixels to skip
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                for (int i = 0; i < bcd; ++i)
                                {
                                    bytesDecoded.Add(0); // TODO: What would we fill with?
                                }
                                throw new NotImplementedException("untested");
                                break;
                            }
                        case 5:
                            {
                                // bcd is count of the number of scan lines to skip (0 means go directly to next scan line)
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                int numLineToFill = bcd > 0 ? bcd : 1;
                                for (int line = 0; line < numLineToFill; ++line)
                                {
                                    int leftInScanLine = _width - (bytesDecoded.Count % _width);
                                    for (int i = 0; i < leftInScanLine; ++i)
                                    {
                                        bytesDecoded.Add(0); // TODO: What would we fill with?
                                    }
                                }

                                throw new NotImplementedException("untested");
                                break;
                            }
                        case 6:
                            {
                                // fill scan lines
                                byte fillType = (byte)(bytes[byteIndex] & 0x0F);
                                switch (fillType)
                                {
                                    case 0:
                                        {
                                            // fill next cd scan lines with ef
                                            byte ef = bytes[byteIndex += 1];

                                            break;
                                        }
                                    case 1:
                                        {
                                            // fill next cd scan lines with efgh
                                            byte ef = bytes[byteIndex += 1];
                                            byte gh = bytes[byteIndex += 1];

                                            break;
                                        }
                                    case 2:
                                        {
                                            // fill next cd scan lines with efghijkl
                                            byte ef = bytes[byteIndex += 1];
                                            byte gh = bytes[byteIndex += 1];
                                            byte ij = bytes[byteIndex += 1];
                                            byte kl = bytes[byteIndex += 1];

                                            break;
                                        }
                                }
                                throw new NotImplementedException("fill scan lines");
                                break;
                            }
                        case 15:
                            // end of bitmap, stop here!
                            return;
                        default:
                            throw new RLEException($"Invalid escape code {code}!");
                    }
                }
                else if (b == -1)
                {
                    //               bytes: 1  2  3  4  5
                    //                    : -1 ab cd ef gh ij
                    // following byte is byte-count for word/long data (pos/neg)
                    byteIndex += 1;
                    int numToRepeat = bytes[byteIndex];
                    if (numToRepeat > 0) // next 16 bits repeated
                    {
                        byteIndex += 1;
                        for (int i = 0; i < numToRepeat; ++i)
                        {
                            bytesDecoded.Add(bytes[byteIndex]);
                            bytesDecoded.Add(bytes[byteIndex + 1]);
                        }
                        byteIndex += 1;
                    }
                    else // next 32 bits repeated
                    {
                        numToRepeat *= -1;
                        byteIndex += 1;
                        for (int i = 0; i < numToRepeat; ++i)
                        {
                            bytesDecoded.Add(bytes[byteIndex]);
                            bytesDecoded.Add(bytes[byteIndex + 1]);
                            bytesDecoded.Add(bytes[byteIndex + 2]);
                            bytesDecoded.Add(bytes[byteIndex + 3]);
                        }
                        byteIndex += 3;
                    }
                }
                else if (b == -2)
                {
                    //               bytes: 1  2  3  4  5
                    //                    : -2 ab cd ef gh
                    // next byte is byte-count of pixels to skip, count of 0 means use LUT
                    byteIndex += 1;
                    int numToSkip = bytes[byteIndex];
                    if (numToSkip != 0)
                    {
                        for (int i = 0; i < numToSkip; ++i)
                        {
                            bytesDecoded.Add(0); // TODO: What would we fill with?
                        }
                    }
                    else
                    {
                        // 0 means use LUT encoding for this scan line
                        // cd is table size, or zero for same as last table
                        // the table is found as the next cd bytes
                        byteIndex += 1;
                        int tableSize = bytes[byteIndex];
                        if (tableSize != 0)
                        {
                            lastTableBytes.Clear();
                            for (int i = 0; i < tableSize; ++i)
                            {
                                byteIndex += 1;
                                lastTableBytes.Add(bytes[byteIndex]);
                            }
                        }
                    }
                }
                else if (b == -128)
                {
                    // repeat the following byte out to the completion of this scan line (end of row?)
                    byteIndex += 1;
                    byte repeatB = bytes[byteIndex];
                    int leftInScanLine = _width - (bytesDecoded.Count % _width);
                    for (int i = 0; i < leftInScanLine; ++i)
                    {
                        bytesDecoded.Add(repeatB);
                    }
                    throw new NotImplementedException("untested");
                }
                else
                {
                    // Repeat the following byte N times
                    int numToRepeat = b * -1;
                    byteIndex += 1;
                    byte repeatB = bytes[byteIndex];
                    for (int i = 0; i < numToRepeat; ++i)
                    {
                        bytesDecoded.Add(repeatB);
                    }
                }
            }
        }

        public void Save(string filename)
        {
            byte[] bytes = GetBitmapBytes();
            File.WriteAllBytes(filename, bytes);
        }
    }

    public record WinColors
    {
        // Palette from https://lospec.com/palette-list/windows-95-256-colours
        public static readonly RawColorBytes[] Palette256 =
        {
            new(0x000000),
            new(0x800000),
            new(0x008000),
            new(0x808000),
            new(0x000080),
            new(0x800080),
            new(0x008080),
            new(0xc0c0c0),
            new(0xc0dcc0),
            new(0xa6caf0),
            new(0x2a3faa),
            new(0x2a3fff),
            new(0x2a5f00),
            new(0x2a5f55),
            new(0x2a5faa),
            new(0x2a5fff),
            new(0x2a7f00),
            new(0x2a7f55),
            new(0x2a7faa),
            new(0x2a7fff),
            new(0x2a9f00),
            new(0x2a9f55),
            new(0x2a9faa),
            new(0x2a9fff),
            new(0x2abf00),
            new(0x2abf55),
            new(0x2abfaa),
            new(0x2abfff),
            new(0x2adf00),
            new(0x2adf55),
            new(0x2adfaa),
            new(0x2adfff),
            new(0x2aff00),
            new(0x2aff55),
            new(0x2affaa),
            new(0x2affff),
            new(0x550000),
            new(0x550055),
            new(0x5500aa),
            new(0x5500ff),
            new(0x551f00),
            new(0x551f55),
            new(0x551faa),
            new(0x551fff),
            new(0x553f00),
            new(0x553f55),
            new(0x553faa),
            new(0x553fff),
            new(0x555f00),
            new(0x555f55),
            new(0x555faa),
            new(0x555fff),
            new(0x557f00),
            new(0x557f55),
            new(0x557faa),
            new(0x557fff),
            new(0x559f00),
            new(0x559f55),
            new(0x559faa),
            new(0x559fff),
            new(0x55bf00),
            new(0x55bf55),
            new(0x55bfaa),
            new(0x55bfff),
            new(0x55df00),
            new(0x55df55),
            new(0x55dfaa),
            new(0x55dfff),
            new(0x55ff00),
            new(0x55ff55),
            new(0x55ffaa),
            new(0x55ffff),
            new(0x7f0000),
            new(0x7f0055),
            new(0x7f00aa),
            new(0x7f00ff),
            new(0x7f1f00),
            new(0x7f1f55),
            new(0x7f1faa),
            new(0x7f1fff),
            new(0x7f3f00),
            new(0x7f3f55),
            new(0x7f3faa),
            new(0x7f3fff),
            new(0x7f5f00),
            new(0x7f5f55),
            new(0x7f5faa),
            new(0x7f5fff),
            new(0x7f7f00),
            new(0x7f7f55),
            new(0x7f7faa),
            new(0x7f7fff),
            new(0x7f9f00),
            new(0x7f9f55),
            new(0x7f9faa),
            new(0x7f9fff),
            new(0x7fbf00),
            new(0x7fbf55),
            new(0x7fbfaa),
            new(0x7fbfff),
            new(0x7fdf00),
            new(0x7fdf55),
            new(0x7fdfaa),
            new(0x7fdfff),
            new(0x7fff00),
            new(0x7fff55),
            new(0x7fffaa),
            new(0x7fffff),
            new(0xaa0000),
            new(0xaa0055),
            new(0xaa00aa),
            new(0xaa00ff),
            new(0xaa1f00),
            new(0xaa1f55),
            new(0xaa1faa),
            new(0xaa1fff),
            new(0xaa3f00),
            new(0xaa3f55),
            new(0xaa3faa),
            new(0xaa3fff),
            new(0xaa5f00),
            new(0xaa5f55),
            new(0xaa5faa),
            new(0xaa5fff),
            new(0xaa7f00),
            new(0xaa7f55),
            new(0xaa7faa),
            new(0xaa7fff),
            new(0xaa9f00),
            new(0xaa9f55),
            new(0xaa9faa),
            new(0xaa9fff),
            new(0xaabf00),
            new(0xaabf55),
            new(0xaabfaa),
            new(0xaabfff),
            new(0xaadf00),
            new(0xaadf55),
            new(0xaadfaa),
            new(0xaadfff),
            new(0xaaff00),
            new(0xaaff55),
            new(0xaaffaa),
            new(0xaaffff),
            new(0xd40000),
            new(0xd40055),
            new(0xd400aa),
            new(0xd400ff),
            new(0xd41f00),
            new(0xd41f55),
            new(0xd41faa),
            new(0xd41fff),
            new(0xd43f00),
            new(0xd43f55),
            new(0xd43faa),
            new(0xd43fff),
            new(0xd45f00),
            new(0xd45f55),
            new(0xd45faa),
            new(0xd45fff),
            new(0xd47f00),
            new(0xd47f55),
            new(0xd47faa),
            new(0xd47fff),
            new(0xd49f00),
            new(0xd49f55),
            new(0xd49faa),
            new(0xd49fff),
            new(0xd4bf00),
            new(0xd4bf55),
            new(0xd4bfaa),
            new(0xd4bfff),
            new(0xd4df00),
            new(0xd4df55),
            new(0xd4dfaa),
            new(0xd4dfff),
            new(0xd4ff00),
            new(0xd4ff55),
            new(0xd4ffaa),
            new(0xd4ffff),
            new(0xff0055),
            new(0xff00aa),
            new(0xff1f00),
            new(0xff1f55),
            new(0xff1faa),
            new(0xff1fff),
            new(0xff3f00),
            new(0xff3f55),
            new(0xff3faa),
            new(0xff3fff),
            new(0xff5f00),
            new(0xff5f55),
            new(0xff5faa),
            new(0xff5fff),
            new(0xff7f00),
            new(0xff7f55),
            new(0xff7faa),
            new(0xff7fff),
            new(0xff9f00),
            new(0xff9f55),
            new(0xff9faa),
            new(0xff9fff),
            new(0xffbf00),
            new(0xffbf55),
            new(0xffbfaa),
            new(0xffbfff),
            new(0xffdf00),
            new(0xffdf55),
            new(0xffdfaa),
            new(0xffdfff),
            new(0xffff55),
            new(0xffffaa),
            new(0xccccff),
            new(0xffccff),
            new(0x33ffff),
            new(0x66ffff),
            new(0x99ffff),
            new(0xccffff),
            new(0x007f00),
            new(0x007f55),
            new(0x007faa),
            new(0x007fff),
            new(0x009f00),
            new(0x009f55),
            new(0x009faa),
            new(0x009fff),
            new(0x00bf00),
            new(0x00bf55),
            new(0x00bfaa),
            new(0x00bfff),
            new(0x00df00),
            new(0x00df55),
            new(0x00dfaa),
            new(0x00dfff),
            new(0x00ff55),
            new(0x00ffaa),
            new(0x2a0000),
            new(0x2a0055),
            new(0x2a00aa),
            new(0x2a00ff),
            new(0x2a1f00),
            new(0x2a1f55),
            new(0x2a1faa),
            new(0x2a1fff),
            new(0x2a3f00),
            new(0x2a3f55),
            new(0xfffbf0),
            new(0xa0a0a4),
            new(0x808080),
            new(0xff0000),
            new(0x00ff00),
            new(0xffff00),
            new(0x0000ff),
            new(0xff00ff),
            new(0x00ffff),
            new(0xffffff)
        };
    }
}
