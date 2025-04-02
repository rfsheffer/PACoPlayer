// Copyright (c) GregDom LLC. All Rights Reserved.
// This file is licensed for use under the MIT license.

namespace PACoPlayer.Decoder.Records;

public record Color
{
    public byte Red { get; init; }
    public byte Green { get; init; }
    public byte Blue { get; init; }

    public Color()
    {
    }

    public Color(int hex)
    {
        Red = (byte)(hex >> 16);
        Green = (byte)((hex >> 8) & 0x0000FF);
        Blue = (byte)(hex & 0x0000FF);
    }

    public override string ToString() =>
        $"#{Red:X2}{Green:X2}{Blue:X2}";
}
