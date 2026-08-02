// Adapted from BIMCamel IFC Exporter (MIT License)
// https://github.com/mrshoma99-rgb/bimcamel-ifc-exporter
using System;
using System.Numerics;

namespace PHDNavisTools.Core
{
    /// <summary>
    /// Converts a .NET Guid to the 22-character IFC GlobalId encoding (base-64 over the IFC alphabet).
    /// The conversion is deterministic: same input Guid always yields the same 22-char string,
    /// enabling stable GlobalIds across re-exports.
    /// </summary>
    public static class IfcGuid
    {
        private const string Alphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        public static string ToIfcGuid(Guid guid)
        {
            // .NET Guid.ToByteArray() stores Data1/2/3 little-endian, Data4 big-endian.
            // Reorder to canonical big-endian so the GUID reads as a single 128-bit number.
            var b = guid.ToByteArray();
            var be = new byte[16]
            {
                b[3], b[2], b[1], b[0],
                b[5], b[4],
                b[7], b[6],
                b[8], b[9], b[10], b[11],
                b[12], b[13], b[14], b[15]
            };

            // BigInteger is little-endian; append 0x00 to force positive.
            var le = new byte[17];
            for (int i = 0; i < 16; i++) le[i] = be[15 - i];
            le[16] = 0;
            var value = new BigInteger(le);

            // Emit 22 base-64 digits (22×6 = 132 bits; top digit holds 2 bits, rest 6 bits each).
            var chars = new char[22];
            for (int i = 21; i >= 0; i--)
            {
                value = BigInteger.DivRem(value, 64, out var rem);
                chars[i] = Alphabet[(int)rem];
            }
            return new string(chars);
        }
    }
}
