// Copyright (c) GregDom LLC. All Rights Reserved.
// This file is licensed for use under the MIT license.

using System;
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

    public class PixelWriter
    {
        // The size of the original image
        private readonly Rectangle _size;

        // The origin inside the image to write pixels to
        private readonly Point _origin;

        // The area inside the image to write pixels to, offset by _origin
        private readonly Rectangle _area;

        // Image bytes for bmp output (B, G, R, A)
        private readonly byte[] _imageBytes;

        // The palette used to convert pixel palette indicies into colors
        private readonly Color[] _palette;

        // The pixels themselves (_width * _height = length)
        private readonly byte[] _pixels;

        // The current pixel within the area
        private int _currentPixel;

        public PixelWriter(Rectangle size, Point origin, Rectangle area, Color[] palette, byte[] pixels, byte[] imageBytes)
        {
            _size = size;
            _origin = origin;
            _area = area;
            _palette = palette;
            _pixels = pixels;
            _imageBytes = imageBytes;
        }

        public void PushPixel(byte paletteIndex)
        {
            if(_currentPixel >= _area.Width * _area.Height)
            {
                throw new RLEException("Pushing pixels outside the drawing area!");
            }

            int x = _origin.Left + (_currentPixel % _area.Width);
            int y = _origin.Top + (_currentPixel / _area.Width);
            int pixelIndex = x + (y * _size.Width);

            _pixels[pixelIndex] = paletteIndex;

            Color pixelColor = _palette[paletteIndex];
            int imageOffset = pixelIndex * 4;
            _imageBytes[imageOffset + 0] = pixelColor.Blue;
            _imageBytes[imageOffset + 1] = pixelColor.Green;
            _imageBytes[imageOffset + 2] = pixelColor.Red;

            _currentPixel += 1;
        }

        public void SkipPixel()
        {
            if (_currentPixel >= _area.Width * _area.Height)
            {
                throw new RLEException("skipping pixels outside the drawing area!");
            }

            _currentPixel += 1;
        }

        public void SkipScanLine()
        {
            if (_currentPixel >= _area.Width * _area.Height)
            {
                throw new RLEException("skipping scanlines outside the drawing area!");
            }

            _currentPixel += _area.Width - (_currentPixel % _area.Width);
        }

        public int PixelsLeftInScanline() => _area.Width - (_currentPixel % _area.Width);
    }

    public class RLEBitmap
    {
        // Width and height in pixels
        private readonly Rectangle _size;

        // Image bytes for bmp output (B, G, R, A)
        private readonly byte[] _imageBytes;

        // The palette used to convert pixel palette indicies into colors
        private readonly Color[] _palette;

        // The pixels themselves (_width * _height = length)
        private readonly byte[] _pixels;

        public RLEBitmap(Rectangle size, Color[] palette)
        {
            _size = size;
            _imageBytes = new byte[_size.Width * _size.Height * 4];
            _palette = palette;
            if(_palette.Length != 256)
            {
                // Default to using the quicktime palette (might want an option for this)
                _palette = ColorPalettes256.QuickTime;
            }
            _pixels = new byte[_size.Width * _size.Height];
        }

        private byte[] GetBitmapBytes()
        {
            const int imageHeaderSize = 54;
            byte[] bmpBytes = new byte[_imageBytes.Length + imageHeaderSize];
            bmpBytes[0] = (byte)'B';
            bmpBytes[1] = (byte)'M';
            bmpBytes[14] = 40;
            Array.Copy(BitConverter.GetBytes(bmpBytes.Length), 0, bmpBytes, 2, 4);
            Array.Copy(BitConverter.GetBytes(imageHeaderSize), 0, bmpBytes, 10, 4);
            Array.Copy(BitConverter.GetBytes(_size.Width), 0, bmpBytes, 18, 4);
            Array.Copy(BitConverter.GetBytes(_size.Height), 0, bmpBytes, 22, 4);
            Array.Copy(BitConverter.GetBytes(32), 0, bmpBytes, 28, 2);
            Array.Copy(BitConverter.GetBytes(_imageBytes.Length), 0, bmpBytes, 34, 4);
            Array.Copy(_imageBytes, 0, bmpBytes, imageHeaderSize, _imageBytes.Length);
            return bmpBytes;
        }

        public void Save(string filename)
        {
            byte[] bytes = GetBitmapBytes();
            File.WriteAllBytes(filename, bytes);
        }

        /**
         * Decodes the RLE into bitmap data
         */
        public void DecodeRLE(Point origin, Rectangle size, CompressionKind compression, byte[] bytes)
        {
            if(compression == CompressionKind.None)
            {
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
            //PixelWriter pixelWriter = new PixelWriter(origin, size, _palette, _pixels, _imageBytes);

            // Compact LUT
            byte[] clrs = new byte[16];

            int byteIndex;
            bool endOfBitmap = false;
            for (byteIndex = 0; byteIndex < bytes.Length && !endOfBitmap;)
            {
                sbyte b = (sbyte)bytes[byteIndex++]; // descriptor byte
                if (b > 0)
                {
                    // N bytes of uncompressed data follow
                    int count = b;
                    for (int i = 0; i < count; ++i)
                    {
                        pixels.Add(bytes[byteIndex++]);
                    }
                }
                else if (b == 0)
                {
                    //               bytes: 1  2  3  4  5  6  7
                    // generic escape code: 0  ab cd ef gh ij kl
                    byte code = (byte)(bytes[byteIndex] >> 4);
                    switch (code)
                    {
                        case 0:
                            {
                                // bcd is count for uncompressed data
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 2;
                                for (int i = 0; i < bcd; ++i)
                                {
                                    pixels.Add(bytes[byteIndex++]);
                                }
                                break;
                            }
                        case 1:
                            {
                                // bcd is count for byte-data ef
                                int bcd = ((bytes[byteIndex] & 0x0F) << 8) | bytes[byteIndex + 1];
                                byteIndex += 2;
                                byte ef = bytes[byteIndex++];
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
                                byteIndex += 2;
                                byte ef = bytes[byteIndex++];
                                byte gh = bytes[byteIndex++];
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
                                byteIndex += 2;
                                byte ef = bytes[byteIndex++];
                                byte gh = bytes[byteIndex++];
                                byte ij = bytes[byteIndex++];
                                byte kl = bytes[byteIndex++];
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
                                byteIndex += 2;
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
                                byteIndex += 2;
                                int numLineToFill = bcd > 0 ? bcd : 1;
                                for (int line = 0; line < numLineToFill; ++line)
                                {
                                    int leftInScanLine = _size.Width - (pixels.Count % _size.Width);
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
                                /*byte fillType = (byte)(bytes[byteIndex++] & 0x0F);
                                switch (fillType)
                                {
                                    case 0:
                                        {
                                            // fill next cd scan lines with ef
                                            byte ef = bytes[byteIndex++];

                                            break;
                                        }
                                    case 1:
                                        {
                                            // fill next cd scan lines with efgh
                                            byte ef = bytes[byteIndex++];
                                            byte gh = bytes[byteIndex++];

                                            break;
                                        }
                                    case 2:
                                        {
                                            // fill next cd scan lines with efghijkl
                                            byte ef = bytes[byteIndex++];
                                            byte gh = bytes[byteIndex++];
                                            byte ij = bytes[byteIndex++];
                                            byte kl = bytes[byteIndex++];

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
                    int numToRepeat = bytes[byteIndex++]; // ab
                    if (numToRepeat < 128) // next 16 bits repeated
                    {
                        byte cd = bytes[byteIndex++];
                        byte ef = bytes[byteIndex++];
                        for (int i = 0; i < numToRepeat; ++i)
                        {
                            pixels.Add(cd);
                            pixels.Add(ef);
                        }
                    }
                    else // next 32 bits repeated
                    {
                        numToRepeat = 256 - numToRepeat;
                        byte cd = bytes[byteIndex++];
                        byte ef = bytes[byteIndex++];
                        byte gh = bytes[byteIndex++];
                        byte ij = bytes[byteIndex++];
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
                    // next byte is byte-count of pixels to skip, count of 0 means use compact RLE
                    int numToSkip = bytes[byteIndex++];
                    if (numToSkip != 0)
                    {
                        for (int i = 0; i < numToSkip; ++i)
                        {
                            pixels.Add(0); // TODO: Should fill with previous pixel
                        }
                    }
                    else
                    {
                        // Get the amount of lookup data
                        uint mask = (uint)(bytes[byteIndex] << 8) | bytes[byteIndex + 1];
                        byteIndex += 2;

                        for (int i = 0; i < 16; i++, mask >>= 1)
                        {
                            if ((mask & 1) != 0)
                            {
                                clrs[i] = bytes[byteIndex++];
                            }
                        }

                        // Fill in the rest of the scan line by parsing tightly packed RLE and using lookup data
                        int pixelsLeftInScan = _size.Width - (pixels.Count % _size.Width);
                        while (pixelsLeftInScan > 0)
                        {
                            short op = bytes[byteIndex++];
                            short len = (short)(op & 0xF);
                            op >>= 4;
                            if (op == 0)
                            {
                                op = len;
                                len = (sbyte)bytes[byteIndex++];
                                switch (op)
                                {
                                    case 0:
                                        {
                                            while (len > 0)
                                            {
                                                len--;
                                                short c = bytes[byteIndex++];
                                                pixels.Add(clrs[c >> 4]); --pixelsLeftInScan;
                                                if (len == 0)
                                                {
                                                    break;
                                                }
                                                len--;
                                                pixels.Add(clrs[c & 0xF]); --pixelsLeftInScan;
                                            }
                                            break;
                                        }
                                    case 1:
                                        {
                                            short c = bytes[byteIndex++];
                                            while (len > 0)
                                            {
                                                len--;
                                                pixels.Add(clrs[c & 0xF]); --pixelsLeftInScan;
                                            }
                                            break;
                                        }
                                    case 2:
                                        {
                                            short c = bytes[byteIndex++];
                                            byte c1 = clrs[c >> 4];
                                            byte c2 = clrs[c & 0xF];
                                            while (len > 0)
                                            {
                                                len--;
                                                pixels.Add(c1); --pixelsLeftInScan;
                                                pixels.Add(c2); --pixelsLeftInScan;
                                            }
                                            break;
                                        }
                                    case 3:
                                        {
                                            short c = bytes[byteIndex++];
                                            byte c1 = clrs[c >> 4];
                                            byte c2 = clrs[c & 0xF];
                                            c = bytes[byteIndex++];
                                            byte c3 = clrs[c >> 4];
                                            byte c4 = clrs[c & 0xF];
                                            while (len > 0)
                                            {
                                                len--;
                                                pixels.Add(c1); --pixelsLeftInScan;
                                                pixels.Add(c2); --pixelsLeftInScan;
                                                pixels.Add(c3); --pixelsLeftInScan;
                                                pixels.Add(c4); --pixelsLeftInScan;
                                            }
                                            break;
                                        }
                                    case 4:
                                        {
                                            while (len > 0)
                                            {
                                                len--;
                                                pixels.Add(0); --pixelsLeftInScan;
                                            }
                                            break;
                                        }
                                    case 5:
                                        {
                                            // This is like skipping to the end of the scan line so just break out?
                                            pixelsLeftInScan = 0;
                                            break;
                                        }
                                }
                            }
                            else if (op < 8) // copy 1-7 colors
                            {
                                pixels.Add(clrs[len]); --pixelsLeftInScan;
                                op--;
                                while(op > 0)
                                {
                                    --op;

                                    short c = bytes[byteIndex++];
                                    pixels.Add(clrs[c >> 4]); --pixelsLeftInScan;
                                    if (op == 0)
                                    {
                                        break;
                                    }

                                    pixels.Add(clrs[c & 0xF]); --pixelsLeftInScan;
                                    --op;
                                }
                            }
                            else if (op < 14) // repeat color
                            {
                                op = (short)(16 - op);
                                while(op > 0)
                                {
                                    --op;
                                    pixels.Add(clrs[len]); --pixelsLeftInScan;
                                }
                            }
                            else if (op < 15) // skip number of pixels in low nibbel
                            {
                                while (len > 0)
                                {
                                    --len;
                                    pixels.Add(0); --pixelsLeftInScan; // TODO: Should fill with previous pixel
                                }
                            }
                            else
                            {
                                if (len < 8) // Pair run
                                {
                                    short c = bytes[byteIndex++];
                                    byte c1 = clrs[c >> 4];
                                    byte c2 = clrs[c & 0xF];
                                    while (len > 0)
                                    {
                                        len--;
                                        pixels.Add(c1); --pixelsLeftInScan;
                                        pixels.Add(c2); --pixelsLeftInScan;
                                    }
                                }
                                else // Quad run
                                {
                                    len = (sbyte)(16 - len);
                                    short c = bytes[byteIndex++];

                                    byte c1 = clrs[c >> 4];
                                    byte c2 = clrs[c & 0xF];
                                    c = bytes[byteIndex++];
                                    byte c3 = clrs[c >> 4];
                                    byte c4 = clrs[c & 0xF];
                                    while (len > 0)
                                    {
                                        len--;
                                        pixels.Add(c1); --pixelsLeftInScan;
                                        pixels.Add(c2); --pixelsLeftInScan;
                                        pixels.Add(c3); --pixelsLeftInScan;
                                        pixels.Add(c4); --pixelsLeftInScan;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (b == -128)
                {
                    // repeat the following byte out to the completion of this scan line (end of row?)
                    byte repeatB = bytes[byteIndex++];
                    int leftInScanLine = _size.Width - (pixels.Count % _size.Width);
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
                    byte repeatB = bytes[byteIndex++];
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
            if(pixels.Count < _size.Width * _size.Height)
            {
                throw new RLEException("Invalid number of decoded bytes!");
            }

            // TODO: Image bytes can just be updated as pixels is updated above.
            //       Will implement with the pixel writer
            int totalPixels = _size.Width * _size.Height;
            for (int decodedIndex = 0; decodedIndex < totalPixels; decodedIndex++)
            {
                int x = decodedIndex % _size.Width;
                int y = _size.Height - (decodedIndex / _size.Width) - 1; // The output bytesDecoded is flipped vertically

                Color color = _palette[pixels[x + (y * _size.Width)]];

                int byteOffset = decodedIndex * 4;
                _imageBytes[byteOffset + 0] = color.Blue;
                _imageBytes[byteOffset + 1] = color.Green;
                _imageBytes[byteOffset + 2] = color.Red;
            }
        }
    }
}
