﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Numerics;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public struct MemoryPoolIterator2
    {
        private MemoryPoolBlock2 _block;
        private int _index;

        public MemoryPoolIterator2(MemoryPoolBlock2 block)
        {
            _block = block;
            _index = _block?.Start ?? 0;
        }
        public MemoryPoolIterator2(MemoryPoolBlock2 block, int index)
        {
            _block = block;
            _index = index;
        }

        public bool IsDefault => _block == null;

        public bool IsEnd
        {
            get
            {
                if (_block == null)
                {
                    return true;
                }
                else if (_index < _block.End)
                {
                    return false;
                }
                else
                {
                    var block = _block.Next;
                    while (block != null)
                    {
                        if (block.Start < block.End)
                        {
                            return false; // subsequent block has data - IsEnd is false
                        }
                        block = block.Next;
                    }
                    return true;
                }
            }
        }

        public MemoryPoolBlock2 Block => _block;

        public int Index => _index;

        public int Take()
        {
            var block = _block;
            if (block == null)
            {
                return -1;
            }

            var index = _index;

            if (index < block.End)
            {
                _index = index + 1;
                return block.Array[index];
            }

            do
            {
                if (block.Next == null)
                {
                    return -1;
                }
                else
                {
                    block = block.Next;
                    index = block.Start;
                }

                if (index < block.End)
                {
                    _block = block;
                    _index = index + 1;
                    return block.Array[index];
                }
            } while (true);
        }

        public int Peek()
        {
            var block = _block;
            if (block == null)
            {
                return -1;
            }

            var index = _index;

            if (index < block.End)
            {
                return block.Array[index];
            }

            do
            {
                if (block.Next == null)
                {
                    return -1;
                }
                else
                {
                    block = block.Next;
                    index = block.Start;
                }

                if (index < block.End)
                {
                    return block.Array[index];
                }
            } while (true);
        }

        public unsafe long PeekLong()
        {
            if (_block == null)
            {
                return -1;
            }
            else if (_block.End - _index >= sizeof(long))
            {
                fixed (byte* ptr = _block.Array)
                {
                    return *(long*)(ptr + _index);
                }
            }
            else if (_block.Next == null)
            {
                return -1;
            }
            else
            {
                var blockBytes = _block.End - _index;
                var nextBytes = sizeof(long) - blockBytes;

                if (_block.Next.End - _block.Next.Start < nextBytes)
                {
                    return -1;
                }

                long blockLong;
                fixed (byte* ptr = _block.Array)
                {
                    blockLong = *(long*)(ptr + _block.End - sizeof(long));
                }

                long nextLong;
                fixed (byte* ptr = _block.Next.Array)
                {
                    nextLong = *(long*)(ptr + _block.Next.Start);
                }

                return (blockLong >> (sizeof(long) - blockBytes) * 8) | (nextLong << (sizeof(long) - nextBytes) * 8);
            }
        }

        public int Seek(Vector<byte> byte0Vector)
        {
            if (IsDefault)
            {
                return -1;
            }

            var block = _block;
            var index = _index;
            var array = block.Array;
            while (true)
            {
                while (block.End == index)
                {
                    if (block.Next == null)
                    {
                        _block = block;
                        _index = index;
                        return -1;
                    }
                    block = block.Next;
                    index = block.Start;
                    array = block.Array;
                }
                while (block.End != index)
                {
                    var following = block.End - index;
                    if (following >= Vector<byte>.Count)
                    {
                        var data = new Vector<byte>(array, index);
                        var byte0Equals = Vector.Equals(data, byte0Vector);

                        if (byte0Equals.Equals(Vector<byte>.Zero))
                        {
                            index += Vector<byte>.Count;
                            continue;
                        }

                        _block = block;
                        _index = index + FindFirstEqualByte(byte0Equals);
                        return byte0Vector[0];
                    }

                    var byte0 = byte0Vector[0];

                    while (following > 0)
                    {
                        if (block.Array[index] == byte0)
                        {
                            _block = block;
                            _index = index;
                            return byte0;
                        }
                        following--;
                        index++;
                    }
                }
            }
        }

        public int Seek(Vector<byte> byte0Vector, Vector<byte> byte1Vector)
        {
            if (IsDefault)
            {
                return -1;
            }

            var block = _block;
            var index = _index;
            var array = block.Array;
            while (true)
            {
                while (block.End == index)
                {
                    if (block.Next == null)
                    {
                        _block = block;
                        _index = index;
                        return -1;
                    }
                    block = block.Next;
                    index = block.Start;
                    array = block.Array;
                }
                while (block.End != index)
                {
                    var following = block.End - index;
                    if (following >= Vector<byte>.Count)
                    {
                        var data = new Vector<byte>(array, index);
                        var byte0Equals = Vector.Equals(data, byte0Vector);
                        var byte1Equals = Vector.Equals(data, byte1Vector);
                        int byte0Index = int.MaxValue;
                        int byte1Index = int.MaxValue;

                        if (!byte0Equals.Equals(Vector<byte>.Zero))
                        {
                            byte0Index = FindFirstEqualByte(byte0Equals);
                        }
                        if (!byte1Equals.Equals(Vector<byte>.Zero))
                        {
                            byte1Index = FindFirstEqualByte(byte1Equals);
                        }

                        if (byte0Index == int.MaxValue && byte1Index == int.MaxValue)
                        {
                            index += Vector<byte>.Count;
                            continue;
                        }

                        _block = block;

                        if (byte0Index < byte1Index)
                        {
                            _index = index + byte0Index;
                            return byte0Vector[0];
                        }

                        _index = index + byte1Index;
                        return byte1Vector[0];
                    }

                    byte byte0 = byte0Vector[0];
                    byte byte1 = byte1Vector[0];

                    while (following > 0)
                    {
                        var byteIndex = block.Array[index];
                        if (byteIndex == byte0)
                        {
                            _block = block;
                            _index = index;
                            return byte0;
                        }
                        else if (byteIndex == byte1)
                        {
                            _block = block;
                            _index = index;
                            return byte1;
                        }
                        following--;
                        index++;
                    }
                }
            }
        }

        public int Seek(Vector<byte> byte0Vector, Vector<byte> byte1Vector, Vector<byte> byte2Vector)
        {
            if (IsDefault)
            {
                return -1;
            }

            var block = _block;
            var index = _index;
            var array = block.Array;
            while (true)
            {
                while (block.End == index)
                {
                    if (block.Next == null)
                    {
                        _block = block;
                        _index = index;
                        return -1;
                    }
                    block = block.Next;
                    index = block.Start;
                    array = block.Array;
                }
                while (block.End != index)
                {
                    var following = block.End - index;
                    if (following >= Vector<byte>.Count)
                    {
                        var data = new Vector<byte>(array, index);
                        var byte0Equals = Vector.Equals(data, byte0Vector);
                        var byte1Equals = Vector.Equals(data, byte1Vector);
                        var byte2Equals = Vector.Equals(data, byte2Vector);
                        int byte0Index = int.MaxValue;
                        int byte1Index = int.MaxValue;
                        int byte2Index = int.MaxValue;

                        if (!byte0Equals.Equals(Vector<byte>.Zero))
                        {
                            byte0Index = FindFirstEqualByte(byte0Equals);
                        }
                        if (!byte1Equals.Equals(Vector<byte>.Zero))
                        {
                            byte1Index = FindFirstEqualByte(byte1Equals);
                        }
                        if (!byte2Equals.Equals(Vector<byte>.Zero))
                        {
                            byte2Index = FindFirstEqualByte(byte2Equals);
                        }

                        if (byte0Index == int.MaxValue && byte1Index == int.MaxValue && byte2Index == int.MaxValue)
                        {
                            index += Vector<byte>.Count;
                            continue;
                        }

                        int toReturn, toMove;
                        if (byte0Index < byte1Index)
                        {
                            if (byte0Index < byte2Index)
                            {
                                toReturn = byte0Vector[0];
                                toMove = byte0Index;
                            }
                            else
                            {
                                toReturn = byte2Vector[0];
                                toMove = byte2Index;
                            }
                        }
                        else
                        {
                            if (byte1Index < byte2Index)
                            {
                                toReturn = byte1Vector[0];
                                toMove = byte1Index;
                            }
                            else
                            {
                                toReturn = byte2Vector[0];
                                toMove = byte2Index;
                            }
                        }

                        _block = block;
                        _index = index + toMove;
                        return toReturn;
                    }

                    var byte0 = byte0Vector[0];
                    var byte1 = byte1Vector[0];
                    var byte2 = byte2Vector[0];

                    while (following > 0)
                    {
                        var byteIndex = block.Array[index];
                        if (byteIndex == byte0)
                        {
                            _block = block;
                            _index = index;
                            return byte0;
                        }
                        else if (byteIndex == byte1)
                        {
                            _block = block;
                            _index = index;
                            return byte1;
                        }
                        else if (byteIndex == byte2)
                        {
                            _block = block;
                            _index = index;
                            return byte2;
                        }
                        following--;
                        index++;
                    }
                }
            }
        }

        private static int FindFirstEqualByte(Vector<byte> byteEquals)
        {
            // Quasi-tree search
            var vector64 = Vector.AsVectorInt64(byteEquals);
            for (var i = 0; i < Vector<long>.Count; i++)
            {
                var longValue = vector64[i];
                if (longValue == 0) continue;

                var shift = i << 1;
                var offset = shift << 2;
                var vector32 = Vector.AsVectorInt32(byteEquals);
                if (vector32[shift] != 0)
                {
                    if (byteEquals[offset] != 0) return offset;
                    if (byteEquals[++offset] != 0) return offset;
                    if (byteEquals[++offset] != 0) return offset;
                    return ++offset;
                }
                offset += 4;
                if (byteEquals[offset] != 0) return offset;
                if (byteEquals[++offset] != 0) return offset;
                if (byteEquals[++offset] != 0) return offset;
                return ++offset;
            }
            throw new InvalidOperationException();
        }

        /// <summary>
        /// Save the data at the current location then move to the next available space.
        /// </summary>
        /// <param name="data">The byte to be saved.</param>
        /// <returns>true if the operation successes. false if can't find available space.</returns>
        public bool Put(byte data)
        {
            if (_block == null)
            {
                return false;
            }
            else if (_index < _block.End)
            {
                _block.Array[_index++] = data;
                return true;
            }

            var block = _block;
            var index = _index;
            while (true)
            {
                if (index < block.End)
                {
                    _block = block;
                    _index = index + 1;
                    block.Array[index] = data;
                    return true;
                }
                else if (block.Next == null)
                {
                    return false;
                }
                else
                {
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public int GetLength(MemoryPoolIterator2 end)
        {
            if (IsDefault || end.IsDefault)
            {
                return -1;
            }

            var block = _block;
            var index = _index;
            var length = 0;
            checked
            {
                while (true)
                {
                    if (block == end._block)
                    {
                        return length + end._index - index;
                    }
                    else if (block.Next == null)
                    {
                        throw new InvalidOperationException("end did not follow iterator");
                    }
                    else
                    {
                        length += block.End - index;
                        block = block.Next;
                        index = block.Start;
                    }
                }
            }
        }

        public MemoryPoolIterator2 CopyTo(byte[] array, int offset, int count, out int actual)
        {
            if (IsDefault)
            {
                actual = 0;
                return this;
            }

            var block = _block;
            var index = _index;
            var remaining = count;
            while (true)
            {
                var following = block.End - index;
                if (remaining <= following)
                {
                    actual = count;
                    if (array != null)
                    {
                        Buffer.BlockCopy(block.Array, index, array, offset, remaining);
                    }
                    return new MemoryPoolIterator2(block, index + remaining);
                }
                else if (block.Next == null)
                {
                    actual = count - remaining + following;
                    if (array != null)
                    {
                        Buffer.BlockCopy(block.Array, index, array, offset, following);
                    }
                    return new MemoryPoolIterator2(block, index + following);
                }
                else
                {
                    if (array != null)
                    {
                        Buffer.BlockCopy(block.Array, index, array, offset, following);
                    }
                    offset += following;
                    remaining -= following;
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public void CopyFrom(byte[] data)
        {
            CopyFrom(data, 0, data.Length);
        }

        public void CopyFrom(ArraySegment<byte> buffer)
        {
            CopyFrom(buffer.Array, buffer.Offset, buffer.Count);
        }

        public void CopyFrom(byte[] data, int offset, int count)
        {
            Debug.Assert(_block != null);
            Debug.Assert(_block.Pool != null);
            Debug.Assert(_block.Next == null);
            Debug.Assert(_block.End == _index);

            var pool = _block.Pool;
            var block = _block;
            var blockIndex = _index;

            var bufferIndex = offset;
            var remaining = count;
            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;

            while (remaining > 0)
            {
                if (bytesLeftInBlock == 0)
                {
                    var nextBlock = pool.Lease();
                    block.End = blockIndex;
                    block.Next = nextBlock;
                    block = nextBlock;

                    blockIndex = block.Data.Offset;
                    bytesLeftInBlock = block.Data.Count;
                }

                var bytesToCopy = remaining < bytesLeftInBlock ? remaining : bytesLeftInBlock;

                Buffer.BlockCopy(data, bufferIndex, block.Array, blockIndex, bytesToCopy);

                blockIndex += bytesToCopy;
                bufferIndex += bytesToCopy;
                remaining -= bytesToCopy;
                bytesLeftInBlock -= bytesToCopy;
            }

            block.End = blockIndex;
            _block = block;
            _index = blockIndex;
        }

        public unsafe void CopyFromAscii(string data)
        {
            Debug.Assert(_block != null);
            Debug.Assert(_block.Pool != null);
            Debug.Assert(_block.Next == null);
            Debug.Assert(_block.End == _index);

            var pool = _block.Pool;
            var block = _block;
            var blockIndex = _index;
            var length = data.Length;

            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;
            var bytesLeftInBlockMinusSpan = bytesLeftInBlock - 3;

            fixed (char* pData = data)
            {
                var input = pData;
                var inputEnd = pData + length;
                var inputEndMinusSpan = inputEnd - 3;

                while (input < inputEnd)
                {
                    if (bytesLeftInBlock == 0)
                    {
                        var nextBlock = pool.Lease();
                        block.End = blockIndex;
                        block.Next = nextBlock;
                        block = nextBlock;

                        blockIndex = block.Data.Offset;
                        bytesLeftInBlock = block.Data.Count;
                        bytesLeftInBlockMinusSpan = bytesLeftInBlock - 3;
                    }

                    fixed (byte* pOutput = block.Data.Array)
                    {
                        var output = pOutput + block.End;

                        var copied = 0;
                        for (; input < inputEndMinusSpan && copied < bytesLeftInBlockMinusSpan; copied += 4)
                        {
                            *(output) = (byte)*(input);
                            *(output + 1) = (byte)*(input + 1);
                            *(output + 2) = (byte)*(input + 2);
                            *(output + 3) = (byte)*(input + 3);
                            output += 4;
                            input += 4;
                        }
                        for (; input < inputEnd && copied < bytesLeftInBlock; copied++)
                        {
                            *(output++) = (byte)*(input++);
                        }

                        blockIndex += copied;
                        bytesLeftInBlockMinusSpan -= copied;
                        bytesLeftInBlock -= copied;
                    }
                }
            }

            block.End = blockIndex;
            _block = block;
            _index = blockIndex;
        }
    }
}
