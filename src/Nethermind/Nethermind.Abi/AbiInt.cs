﻿//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Numerics;
using Nethermind.Core.Extensions;
using Nethermind.Int256;

namespace Nethermind.Abi
{
    public class AbiInt : AbiType
    {
        private const int MaxSize = 256;
        private const int MinSize = 0;
        
        public static new readonly AbiInt Int8 = new(8);
        public static new readonly AbiInt Int16 = new(16);
        public static new readonly AbiInt Int32 = new(32);
        public static new readonly AbiInt Int64 = new(64);
        public static new readonly AbiInt Int96 = new(96);
        public static new readonly AbiInt Int256 = new(256);

        static AbiInt()
        {
            RegisterMapping<sbyte>(Int8);
            RegisterMapping<short>(Int16);
            RegisterMapping<int>(Int32);
            RegisterMapping<long>(Int64);
            RegisterMapping<Int256.Int256>(Int256);
        }

        public AbiInt(int length)
        {
            if (length % 8 != 0)
            {
                throw new ArgumentException(nameof(length),
                    $"{nameof(length)} of {nameof(AbiInt)} has to be a multiple of 8");
            }

            if (length > MaxSize)
            {
                throw new ArgumentException(nameof(length),
                    $"{nameof(length)} of {nameof(AbiInt)} has to be less or equal to {MinSize}");
            }

            if (length <= MinSize)
            {
                throw new ArgumentException(nameof(length),
                    $"{nameof(length)} of {nameof(AbiInt)} has to be greater than {MinSize}");
            }

            Length = length;
            Name = $"int{Length}";
            CSharpType = GetCSharpType();
        }

        public int Length { get; }

        public int LengthInBytes => Length / 8;

        public override string Name { get; }
        
        public override (object, int) Decode(byte[] data, int position, bool packed)
        {
            var (value, length) = DecodeInt(data, position, packed);
            
            switch (Length)
            {
                case { } n when n <= 8:
                    return ((sbyte) value, length);
                case { } n when n <= 16:
                    return ((short) value, length);
                case { } n when n <= 32:
                    return ((int) value, length);
                case { } n when n <= 64:
                    return ((long) value, length);
                default:
                    return (value, length);
            }
        }

        public (BigInteger, int) DecodeInt(byte[] data, int position, bool packed)
        {
            int length = (packed ? LengthInBytes : Int256.LengthInBytes);
            byte[] input = data.Slice(position, length);
            return (input.ToSignedBigInteger(length), position + length);
        }

        public override byte[] Encode(object? arg, bool packed)
        {
            if (arg is BigInteger input)
            {
                return input.ToBigEndianByteArray(32);
            }

            throw new AbiException(AbiEncodingExceptionMessage);
        }

        public override Type CSharpType { get; } = typeof(BigInteger);
        
        private Type GetCSharpType()
        {
            switch (Length)
            {
                case { } n when n <= 8:
                    return typeof(sbyte);
                case { } n when n <= 16:
                    return typeof(short);
                case { } n when n <= 32:
                    return typeof(int);
                case { } n when n <= 64:
                    return typeof(long);
                default:
                    return typeof(BigInteger);
            }
        }
    }
}
