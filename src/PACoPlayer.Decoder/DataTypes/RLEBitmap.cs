// Copyright (c) GregDom LLC. All Rights Reserved.
// This file is licensed for use under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using PACoPlayer.Decoder.Records;


namespace PACoPlayer.Decoder.DataTypes
{
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Struct is changing in future")]
    public struct RawColorBytes
    {
        public readonly byte R { get; }
        public readonly byte G { get; }
        public readonly byte B { get; }

        public RawColorBytes(byte r, byte g, byte b)
        {
            (R, G, B) = (r, g, b);
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

#pragma warning disable CA1822 // Mark members as static
#pragma warning disable IDE0059 // Unnecessary assignment of a value
        private void DecodeRLE(byte[] bytes)
        {
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
                    for (int i = 0; i < b; ++i)
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
                                throw new NotImplementedException("bcd is count of the number of pixels to skip");
                                break;
                            }
                        case 5:
                            {
                                // bcd is count of the number of scan lines to skip (0 means go directly to next scan line)
                                throw new NotImplementedException("bcd is count of the number of scan lines to skip (0 means go directly to next scan line)");
                                break;
                            }
                        case 6:
                            {
                                // fill scan lines
                                byte fillType = (byte)(bytes[byteIndex] & 0x0F);
                                switch (fillType)
                                {
                                    case 0:
                                        // fill next cd scan lines with ef
                                        break;
                                    case 1:
                                        // fill next cd scan lines with efgh
                                        break;
                                    case 2:
                                        // fill next cd scan lines with efghijkl
                                        break;
                                }
                                throw new NotImplementedException("fill scan lines");
                                break;
                            }
                        case 15:
                            // end of bitmap, stop here!
                            return;
                    }
                }
                else if (b == -1)
                {
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
                            bytesDecoded.Add(0);
                        }
                    }
                    else
                    {
                        //throw new NotImplementedException("use LUT encoding for this scan line...");
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
                    throw new NotImplementedException("repeat the following byte out to the completion of this scan line (end of row?)");
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
}
