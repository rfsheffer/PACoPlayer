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
        private readonly RawColorBytes[] _palette;

        public RLEBitmap(int width, int height, byte[] bytes, Color solidColor, RawColorBytes[] palette)
        {
            _width = width;
            _height = height;
            _imageBytes = new byte[width * height * 4];
            _solidColor = solidColor;
            _palette = palette;

            if (bytes.Length > 0)
            {
                DecodeRLE(bytes);
            }

            if(palette.Length != 256)
            {
                throw new RLEException("Invalid palette!");
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
            bool endOfBitmap = false;
            for (byteIndex = 0; byteIndex < bytes.Length && !endOfBitmap; byteIndex++)
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
                            endOfBitmap = true;
                            break;
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

            if(!endOfBitmap)
            {
                throw new RLEException("Did not receive end of bitmap flag!");
            }

            // TODO: error on incorrect decoded to pixel count, not just less than.
            if(bytesDecoded.Count < _width * _height)
            {
                throw new RLEException("Invalid number of decoded bytes!");
            }

            int totalPixels = _width * _height;
            for (int decodedIndex = 0; decodedIndex < totalPixels; decodedIndex++)
            {
                int x = decodedIndex % _width;
                int y = _height - (decodedIndex / _width) - 1; // The output bytesDecoded is flipped vertically

                RawColorBytes color = _palette[bytesDecoded[x + (y * _width)]];

                int byteOffset = decodedIndex * 4;
                _imageBytes[byteOffset + 0] = color.B;
                _imageBytes[byteOffset + 1] = color.G;
                _imageBytes[byteOffset + 2] = color.R;
            }
        }

        public void Save(string filename)
        {
            byte[] bytes = GetBitmapBytes();
            File.WriteAllBytes(filename, bytes);
        }
    }

    public record ColorPalettes256
    {
        // From https://lospec.com/palette-list/windows-95-256-colours
        public static readonly RawColorBytes[] Win95 =
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

        // From https://gist.github.com/cesarmiquel/1780ab6078b9735371d1f10a9d60d233
        public static readonly RawColorBytes[] VGA =
        {
            new(0x000000), new(0x0002aa), new(0x14aa00), new(0x00aaaa), new(0xaa0003), new(0xaa00aa), new(0xaa5500), new(0xaaaaaa),
            new(0x555555), new(0x5555ff), new(0x55ff55), new(0x55ffff), new(0xff5555), new(0xfd55ff), new(0xffff55), new(0xffffff),
            new(0x000000), new(0x101010), new(0x202020), new(0x353535), new(0x454545), new(0x555555), new(0x656565), new(0x757575),
            new(0x8a8a8a), new(0x9a9a9a), new(0xaaaaaa), new(0xbababa), new(0xcacaca), new(0xdfdfdf), new(0xefefef), new(0xffffff),
            new(0x0004ff), new(0x4104ff), new(0x8203ff), new(0xbe02ff), new(0xfd00ff), new(0xfe00be), new(0xff0082), new(0xff0041),
            new(0xff0008), new(0xff4105), new(0xff8200), new(0xffbe00), new(0xffff00), new(0xbeff00), new(0x82ff00), new(0x41ff01),
            new(0x24ff00), new(0x22ff42), new(0x1dff82), new(0x12ffbe), new(0x00ffff), new(0x00beff), new(0x0182ff), new(0x0041ff),
            new(0x8282ff), new(0x9e82ff), new(0xbe82ff), new(0xdf82ff), new(0xfd82ff), new(0xfe82df), new(0xff82be), new(0xff829e),
            new(0xff8282), new(0xff9e82), new(0xffbe82), new(0xffdf82), new(0xffff82), new(0xdfff82), new(0xbeff82), new(0x9eff82),
            new(0x82ff82), new(0x82ff9e), new(0x82ffbe), new(0x82ffdf), new(0x82ffff), new(0x82dfff), new(0x82beff), new(0x829eff),
            new(0xbabaff), new(0xcabaff), new(0xdfbaff), new(0xefbaff), new(0xfebaff), new(0xfebaef), new(0xffbadf), new(0xffbaca),
            new(0xffbaba), new(0xffcaba), new(0xffdfba), new(0xffefba), new(0xffffba), new(0xefffba), new(0xdfffba), new(0xcaffbb),
            new(0xbaffba), new(0xbaffca), new(0xbaffdf), new(0xbaffef), new(0xbaffff), new(0xbaefff), new(0xbadfff), new(0xbacaff),
            new(0x010171), new(0x1c0171), new(0x390171), new(0x550071), new(0x710071), new(0x710055), new(0x710039), new(0x71001c),
            new(0x710001), new(0x711c01), new(0x713900), new(0x715500), new(0x717100), new(0x557100), new(0x397100), new(0x1c7100),
            new(0x097100), new(0x09711c), new(0x067139), new(0x037155), new(0x007171), new(0x005571), new(0x003971), new(0x001c71),
            new(0x393971), new(0x453971), new(0x553971), new(0x613971), new(0x713971), new(0x713961), new(0x713955), new(0x713945),
            new(0x713939), new(0x714539), new(0x715539), new(0x716139), new(0x717139), new(0x617139), new(0x557139), new(0x45713a),
            new(0x397139), new(0x397145), new(0x397155), new(0x397161), new(0x397171), new(0x396171), new(0x395571), new(0x394572),
            new(0x515171), new(0x595171), new(0x615171), new(0x695171), new(0x715171), new(0x715169), new(0x715161), new(0x715159),
            new(0x715151), new(0x715951), new(0x716151), new(0x716951), new(0x717151), new(0x697151), new(0x617151), new(0x597151),
            new(0x517151), new(0x51715a), new(0x517161), new(0x517169), new(0x517171), new(0x516971), new(0x516171), new(0x515971),
            new(0x000042), new(0x110041), new(0x200041), new(0x310041), new(0x410041), new(0x410032), new(0x410020), new(0x410010),
            new(0x410000), new(0x411000), new(0x412000), new(0x413100), new(0x414100), new(0x314100), new(0x204100), new(0x104100),
            new(0x034100), new(0x034110), new(0x024120), new(0x014131), new(0x004141), new(0x003141), new(0x002041), new(0x001041),
            new(0x202041), new(0x282041), new(0x312041), new(0x392041), new(0x412041), new(0x412039), new(0x412031), new(0x412028),
            new(0x412020), new(0x412820), new(0x413120), new(0x413921), new(0x414120), new(0x394120), new(0x314120), new(0x284120),
            new(0x204120), new(0x204128), new(0x204131), new(0x204139), new(0x204141), new(0x203941), new(0x203141), new(0x202841),
            new(0x2d2d41), new(0x312d41), new(0x352d41), new(0x3d2d41), new(0x412d41), new(0x412d3d), new(0x412d35), new(0x412d31),
            new(0x412d2d), new(0x41312d), new(0x41352d), new(0x413d2d), new(0x41412d), new(0x3d412d), new(0x35412d), new(0x31412d),
            new(0x2d412d), new(0x2d4131), new(0x2d4135), new(0x2d413d), new(0x2d4141), new(0x2d3d41), new(0x2d3541), new(0x2d3141),
            new(0x000000), new(0x000000), new(0x000000), new(0x000000), new(0x000000), new(0x000000), new(0x000000), new(0x000000)
        };
    }
}
