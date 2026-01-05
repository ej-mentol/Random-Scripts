using System;
using System.IO.Hashing;

namespace TdrExport.Helpers
{
    public class CRC32
    {
        private const uint DefaultPolynomial = 0xedb88320;
        private readonly uint[] _table;
        private readonly bool _useHardware;
        private uint _result = 0xffffffff;

        public CRC32() : this(DefaultPolynomial) { }

        public CRC32(uint polynomial)
        {
            // Use hardware accelerated .NET implementation if standard polynomial is used
            if (polynomial == DefaultPolynomial)
            {
                _useHardware = true;
                return;
            }

            // Fallback to legacy software implementation for custom polynomials
            _useHardware = false;
            _table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc32 = i;
                for (int j = 8; j > 0; j--)
                {
                    if ((crc32 & 1) == 1)
                        crc32 = (crc32 >> 1) ^ polynomial;
                    else
                        crc32 >>= 1;
                }
                _table[i] = crc32;
            }
        }

        public byte[] Hash(byte[] array)
        {
            if (_useHardware)
            {
                // System.IO.Hashing.Crc32 returns 4 bytes in Big-Endian order usually, 
                // but BitConverter.GetBytes returns Little-Endian on Windows (Intel).
                // Let's match the original behavior: it returned BitConverter.GetBytes(~result).
                
                // Crc32.Hash returns the final hash directly.
                byte[] hash = Crc32.Hash(array);
                
                // However, System.IO.Hashing returns Big Endian bytes by standard definition of CRC output?
                // Or Little Endian?
                // Let's verify: The original code does manual XOR and shifts, producing a uint, then calls BitConverter.GetBytes().
                // On x86/x64 (Little Endian), the original code returns [Low, ..., High].
                // Crc32.HashToUInt32 returns the uint value directly.
                
                uint val = Crc32.HashToUInt32(array);
                return BitConverter.GetBytes(val);
            }
            else
            {
                int start = 0;
                int size = array.Length;
                int end = start + size;

                _result = 0xffffffff;

                for (int i = start; i < end; i++)
                {
                    _result = (_result >> 8) ^ _table[array[i] ^ (_result & 0xff)];
                }

                _result = ~_result;

                return BitConverter.GetBytes(_result);
            }
        }
    }
}
