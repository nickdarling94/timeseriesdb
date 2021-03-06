#region COPYRIGHT

/*
 *     Copyright 2009-2012 Yuri Astrakhan  (<Firstname><Lastname>@gmail.com)
 *
 *     This file is part of TimeSeriesDb library
 * 
 *  TimeSeriesDb is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 * 
 *  TimeSeriesDb is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 * 
 *  You should have received a copy of the GNU General Public License
 *  along with TimeSeriesDb.  If not, see <http://www.gnu.org/licenses/>.
 *
 */

#endregion

using System;
using System.Collections.Generic;
using System.Threading;

namespace NYurik.TimeSeriesDb
{
    /// <summary>
    /// Yields buffers that could either be the same instance as previous, or a larger one.
    /// A weak reference will be kept so as to reduce the number of memory allocations.
    /// All public methods are thread safe.
    /// </summary>
    public class BufferProvider<T>
    {
        private WeakReference _buffer;

        /// <summary>
        /// Yield maximum available buffer every time. If the buffer is smaller than initSize,
        /// allocate [initSize] items first, and after growAfter iterations, grow it to the largeSize.
        /// Buffer.Count will always be set to 0
        /// </summary>
        public IEnumerable<Buffer<T>> YieldMaxGrowingBuffer(
            long maxItemCount, int initSize, int growAfter,
            int largeSize)
        {
            if (maxItemCount < 0)
                throw new ArgumentOutOfRangeException("maxItemCount", maxItemCount, "<0");
            if (initSize <= 0)
                throw new ArgumentOutOfRangeException("initSize", initSize, "<=0");
            if (growAfter < 0)
                throw new ArgumentOutOfRangeException("growAfter", growAfter, "<0");
            if (largeSize <= 0)
                throw new ArgumentOutOfRangeException("largeSize", largeSize, "<=0");

            if (maxItemCount == 0)
                yield break;
            
            Buffer<T> buffer = GetBufferRef();

            int size = initSize > maxItemCount ? (int) maxItemCount : initSize;
            if (buffer == null || buffer.Capacity < size)
                buffer = new Buffer<T>(size);

            try
            {
                for (int i = 0; i < growAfter && maxItemCount > 0; i++)
                {
                    buffer.Count = maxItemCount < buffer.Capacity ? (int) maxItemCount : buffer.Capacity;
                    maxItemCount -= buffer.Count;
                    yield return buffer;
                }

                size = largeSize > maxItemCount ? (int) maxItemCount : largeSize;

                if (buffer.Capacity < size)
                    buffer = new Buffer<T>(size);

                while (maxItemCount > 0)
                {
                    buffer.Count = maxItemCount < buffer.Capacity ? (int)maxItemCount : buffer.Capacity;
                    maxItemCount -= buffer.Count;
                    yield return buffer;
                }
            }
            finally
            {
                _buffer = new WeakReference(buffer);
            }
        }

        /// <summary>
        /// Yield a single buffer of a given size or larger.
        /// Buffer.Count will be set to size
        /// </summary>
        public IEnumerable<Buffer<T>> YieldSingleFixedSize(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException("size", size, "<=0");

            Buffer<T> buffer = GetBufferRef();

            if (buffer == null || buffer.Capacity < size)
                buffer = new Buffer<T>(size);

            buffer.Count = size;
            yield return buffer;
        }

        /// <summary>
        /// Yields a sequence of buffers with the count set to:
        /// [blockOne, blockTwo, (growAfter * smallSize), largeSize...]
        /// </summary>
        public IEnumerable<Buffer<T>> YieldFixed(
            int blockOne, int blockTwo, int smallSize, int growAfter, int largeSize)
        {
            if (smallSize <= 0 || largeSize <= 0)
                throw new ArgumentException("smallSize and largeSize must not be 0");

            Buffer<T> buffer = GetBufferRef();

            try
            {
                // allocate blockTwo from the begining
                if (buffer == null || buffer.Capacity < blockOne)
                    buffer = new Buffer<T>(blockTwo);

                buffer.Count = blockOne;
                yield return buffer;

                if (buffer.Capacity < blockTwo)
                    buffer = new Buffer<T>(blockTwo);

                buffer.Count = blockTwo;
                yield return buffer;

                if (buffer.Capacity < smallSize)
                    buffer = new Buffer<T>(smallSize);

                for (int i = 0; i < growAfter; i++)
                {
                    buffer.Count = smallSize;
                    yield return buffer;
                }

                if (buffer.Capacity < largeSize)
                    buffer = new Buffer<T>(largeSize);

                while (true)
                {
                    buffer.Count = largeSize;
                    yield return buffer;
                }
            }
            finally
            {
                if (buffer != null)
                    _buffer = new WeakReference(buffer);
            }
        }

        private Buffer<T> GetBufferRef()
        {
            WeakReference weakRef = Interlocked.Exchange(ref _buffer, null);
            return (weakRef != null ? weakRef.Target as Buffer<T> : null);
        }
    }
}