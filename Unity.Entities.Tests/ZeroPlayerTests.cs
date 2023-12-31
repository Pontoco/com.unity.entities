using System;
using System.Diagnostics;
using Unity.Jobs;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Tests
{
    class ZeroPlayerTests
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void AllowSecondaryWriting()
        {
            AtomicSafetyHandle handle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(handle, false);
            AtomicSafetyHandle.UseSecondaryVersion(ref handle);
            AtomicSafetyHandle.CheckReadAndThrow(handle);
            Assert.Throws<InvalidOperationException>(() => AtomicSafetyHandle.CheckWriteAndThrow(handle));
        }

        [Test]
        public void ReleaseShouldThrow()
        {
            AtomicSafetyHandle handle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.Release(handle);
            Assert.Throws<ObjectDisposedException>(
                () => AtomicSafetyHandle.CheckReadAndThrow(handle));

            Assert.Throws<ObjectDisposedException>(
                () => AtomicSafetyHandle.CheckWriteAndThrow(handle));

            Assert.Throws<ObjectDisposedException>(
                () => AtomicSafetyHandle.CheckExistsAndThrow(handle));
        }

        [Test]
        public void CloneAndReleaseOriginalShouldThrow()
        {
            AtomicSafetyHandle handle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle clone = handle;
            AtomicSafetyHandle.Release(handle);

            Assert.Throws<ObjectDisposedException>(
                () => AtomicSafetyHandle.CheckReadAndThrow(clone));

            Assert.Throws<ObjectDisposedException>(
                () => AtomicSafetyHandle.CheckWriteAndThrow(clone));
        }

        [Test]
        public void CheckDeallocateShouldThrow()
        {
            AtomicSafetyHandle handle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle clone = handle;
            AtomicSafetyHandle.CheckDeallocateAndThrow(clone);
            AtomicSafetyHandle.Release(handle);
            Assert.Throws<ObjectDisposedException>(
                () => AtomicSafetyHandle.CheckDeallocateAndThrow(clone));
        }

#endif
    }
}
