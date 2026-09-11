using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ColorProfileHelperTests
    {
        private static void WriteBigEndianUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IccProfileIsHdr_CicpTagBeforeSmallerUnrelatedTag_UsesCicpSize()
        {
            var data = new byte[180];
            WriteBigEndianUInt32(data, 128, 2);

            const int firstTag = 132;
            WriteBigEndianUInt32(data, firstTag, 0x63696370);
            WriteBigEndianUInt32(data, firstTag + 4, 160);
            WriteBigEndianUInt32(data, firstTag + 8, 12);

            const int secondTag = firstTag + 12;
            WriteBigEndianUInt32(data, secondTag, 0x64657363);
            WriteBigEndianUInt32(data, secondTag + 4, 172);
            WriteBigEndianUInt32(data, secondTag + 8, 4);

            data[169] = 16;

            using var stream = new MemoryStream(data, writable: false);

            Assert.IsTrue(ColorProfileHelper.IccProfileIsHdr(stream));
        }
    }
}
