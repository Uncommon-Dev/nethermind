﻿using System;

namespace Nevermind.Core.Sugar
{
    public static class UInt64Extensions
    {
        public static byte[] ToBigEndianByteArray(this ulong value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }
    }
}