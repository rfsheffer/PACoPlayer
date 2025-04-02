// Copyright (c) GregDom LLC. All Rights Reserved.
// This file is licensed for use under the MIT license.

using PACoPlayer.Decoder.Records;

namespace PACoPlayer.Decoder.DataTypes
{
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
        // Width and height in pixels
        private readonly int _width;
        private readonly int _height;

        // Image bytes for bmp output (B, G, R, A)
        private readonly byte[] _imageBytes;

        // The palette used to convert pixel palette indicies into colors
        private readonly Color[] _palette;

        // The pixels themselves (_width * _height = length)
        private readonly byte[] _pixels;

        public RLEBitmap(int width, int height, Color[] palette)
        {
            _width = width;
            _height = height;
            _imageBytes = new byte[width * height * 4];
            _palette = palette;
            if(_palette.Length != 256)
            {
                // Default to using the quicktime palette (might want an option for this)
                _palette = ColorPalettes256.QuickTime;
            }
            _pixels = new byte[width * height];
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
        public void DecodeRLE(Point origin, Rectangle size, CompressionKind compression, byte[] bytes)
        {
            if(compression != CompressionKind.Rle)
            {
                // Compact coming soon.
                return;
            }

            /*
             * CURRENT ASSUMPTIONS:
             * WORD = 16bits (platforms around 1990)
             * SCAN LINE = bitmap row
             * PIXEL = single decoded entry (Index into the palette)
             * ab cd ef gh ij etc denote bytes and nibbles in order
             */
            // TODO: SWITCH TO USING ARRAY ONCE WE FIX OFFSET ISSUES
            List<byte> pixels = new List<byte>();

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
                        pixels.Add(bytes[byteIndex]);
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
                                    pixels.Add(bytes[byteIndex]);
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
                                    pixels.Add(ef);
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
                                    pixels.Add(ef);
                                    pixels.Add(gh);
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
                                    pixels.Add(ef);
                                    pixels.Add(gh);
                                    pixels.Add(ij);
                                    pixels.Add(kl);
                                }
                                break;
                            }
                        case 4:
                            {
                                // bcd is count of the number of pixels to skip
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 1;
                                for (int i = 0; i < bcd; ++i)
                                {
                                    pixels.Add(0); // TODO: Should fill with previous pixel
                                }
                                break;
                            }
                        case 5:
                            {
                                // bcd is count of the number of scan lines to skip (0 means go directly to next scan line)
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 1;
                                int numLineToFill = bcd > 0 ? bcd : 1;
                                for (int line = 0; line < numLineToFill; ++line)
                                {
                                    int leftInScanLine = _width - (pixels.Count % _width);
                                    for (int i = 0; i < leftInScanLine; ++i)
                                    {
                                        pixels.Add(0); // TODO: Should fill with previous pixel
                                    }
                                }
                                break;
                            }
                        case 6:
                            {
                                // NOTE: Unseen OP
                                // fill scan lines
                                /*byte fillType = (byte)(bytes[byteIndex] & 0x0F);
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
                                break;*/
                                throw new NotImplementedException("fill scan lines");
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
                    int numToRepeat = bytes[byteIndex]; // ab
                    if (numToRepeat < 128) // next 16 bits repeated
                    {
                        byte cd = bytes[byteIndex += 1];
                        byte ef = bytes[byteIndex += 1];
                        for (int i = 0; i < numToRepeat; ++i)
                        {
                            pixels.Add(cd);
                            pixels.Add(ef);
                        }
                    }
                    else // next 32 bits repeated
                    {
                        numToRepeat = 256 - numToRepeat;
                        byte cd = bytes[byteIndex += 1];
                        byte ef = bytes[byteIndex += 1];
                        byte gh = bytes[byteIndex += 1];
                        byte ij = bytes[byteIndex += 1];
                        for (int i = 0; i < numToRepeat; ++i)
                        {
                            pixels.Add(cd);
                            pixels.Add(ef);
                            pixels.Add(gh);
                            pixels.Add(ij);
                        }
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
                            pixels.Add(0); // TODO: Should fill with previous pixel
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
                    int leftInScanLine = _width - (pixels.Count % _width);
                    for (int i = 0; i < leftInScanLine; ++i)
                    {
                        pixels.Add(repeatB);
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
                        pixels.Add(repeatB);
                    }
                }
            }

            if(!endOfBitmap)
            {
                throw new RLEException("Did not receive end of bitmap flag!");
            }

            // TODO: error on incorrect decoded to pixel count, not just less than.
            if(pixels.Count < _width * _height)
            {
                throw new RLEException("Invalid number of decoded bytes!");
            }

            int totalPixels = _width * _height;
            for (int decodedIndex = 0; decodedIndex < totalPixels; decodedIndex++)
            {
                int x = decodedIndex % _width;
                int y = _height - (decodedIndex / _width) - 1; // The output bytesDecoded is flipped vertically

                Color color = _palette[pixels[x + (y * _width)]];

                int byteOffset = decodedIndex * 4;
                _imageBytes[byteOffset + 0] = color.Blue;
                _imageBytes[byteOffset + 1] = color.Green;
                _imageBytes[byteOffset + 2] = color.Red;
            }
        }

        public void Save(string filename)
        {
            byte[] bytes = GetBitmapBytes();
            File.WriteAllBytes(filename, bytes);
        }
    }
}
