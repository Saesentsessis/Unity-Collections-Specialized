using NUnit.Framework;

namespace Unity.Collections.Specialized.Tests
{
    public class StableIndexHandleTests
    {
        [Test]
        public void Null_HasInvalidIndex()
        {
            var handle = StableIndexHandle.Null;

            Assert.AreEqual(-1, handle.Index);
            Assert.AreEqual(0, handle.Version);
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void Equality_ComparesIndexAndVersion()
        {
            var left = new StableIndexHandle(3, 2);
            var right = new StableIndexHandle(3, 2);
            var differentIndex = new StableIndexHandle(4, 2);
            var differentVersion = new StableIndexHandle(3, 3);

            Assert.IsTrue(left.Equals(right));
            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);
            Assert.IsFalse(left.Equals(differentIndex));
            Assert.IsFalse(left.Equals(differentVersion));
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }

        [Test]
        public void EqualsObject_ReturnsFalseForNullOrWrongType()
        {
            var handle = new StableIndexHandle(1, 1);

            Assert.IsFalse(handle.Equals(null));
            Assert.IsFalse(handle.Equals("not-a-handle"));
        }
    }
}
