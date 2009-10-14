using System;
using System.Collections.Generic;
using System.IO;
using NYurik.FastBinTimeseries.CommonCode;

namespace NYurik.FastBinTimeseries
{
    /// <summary>
    /// Helper non-generic class aids in creating a new instance of <see cref="BinUniformTimeseriesFile{T}"/>.
    /// </summary>
    public static class BinUniformTimeseriesFile
    {
        /// <summary>
        /// Uses reflection to create an instance of <see cref="BinUniformTimeseriesFile{T}"/>.
        /// </summary>
        public static IBinUniformTimeseriesFile GenericNew(Type itemType, string fileName, TimeSpan itemTimeSpan,
                                                           UtcDateTime firstTimestamp)
        {
            return (IBinUniformTimeseriesFile)
                   Activator.CreateInstance(
                       typeof (BinUniformTimeseriesFile<>).MakeGenericType(itemType),
                       fileName, itemTimeSpan, firstTimestamp);
        }
    }

    /// <summary>
    /// Object representing a binary-serialized timeseries file with each item with uniform distribution of items
    /// one <see cref="ItemTimeSpan"/> from each other.
    /// </summary>
    public class BinUniformTimeseriesFile<T> : BinaryFile<T>, IBinaryFile<T>, IBinUniformTimeseriesFile,
                                               IStoredUniformTimeseries<T>, IHistFeedInt<T>
    {
        #region Constructors

        /// <summary>
        /// Allow Activator non-public instantiation
        /// </summary>
        protected BinUniformTimeseriesFile()
        {
        }

        /// <summary>
        /// Create new timeseries file. If the file already exists, an <see cref="IOException"/> is thrown.
        /// </summary>
        /// <param name="fileName">A relative or absolute path for the file to create.</param>
        /// <param name="itemTimeSpan">Unit of time each value represents.
        ///   If less than a day, the day must be evenly divisible by this value</param>
        /// <param name="firstTimestamp"></param>
        public BinUniformTimeseriesFile(string fileName, TimeSpan itemTimeSpan, UtcDateTime firstTimestamp)
            : base(fileName)
        {
            ItemTimeSpan = itemTimeSpan;
            FirstTimestamp = firstTimestamp;
        }

        #endregion

        #region Fields

        private UtcDateTime _firstTimestamp;
        private TimeSpan _itemTimeSpan;

        /// <summary>
        /// The timestamp of the first item in the file.
        /// May only be set before the <see cref="BinaryFile.InitializeNewFile"/> is called.
        /// </summary>
        public UtcDateTime FirstTimestamp
        {
            get { return _firstTimestamp; }
            set
            {
                ThrowOnInitialized();
                _firstTimestamp = this.ValidateIndex(value);
            }
        }

        public UtcDateTime FirstUnavailableTimestamp
        {
            get { return FirstTimestamp + ItemTimeSpan.Multiply(Count); }
        }

        /// <summary>
        /// Span of time each item represents.
        /// May only be set before the <see cref="BinaryFile.InitializeNewFile"/> is called.
        /// </summary>
        public TimeSpan ItemTimeSpan
        {
            get { return _itemTimeSpan; }
            set
            {
                ThrowOnInitialized();
                if (value > TimeSpan.FromDays(1))
                    throw new IOException(String.Format("Time slice {0} is > 1 day", value));
                if (TimeSpan.TicksPerDay%value.Ticks != 0)
                    throw new IOException(String.Format("TimeSpan.TicksPerDay must be divisible by time slice {0} ",
                                                        value));
                _itemTimeSpan = value;
            }
        }

        public int AdjustRangeToExistingData(ref UtcDateTime fromInclusive, ref UtcDateTime toExclusive)
        {
            if (fromInclusive < FirstTimestamp)
                fromInclusive = FirstTimestamp;
            else
                fromInclusive = new UtcDateTime(
                    FastBinFileUtils.RoundUpToMultiple(fromInclusive.Ticks, ItemTimeSpan.Ticks));

            if (toExclusive > FirstUnavailableTimestamp)
                toExclusive = FirstUnavailableTimestamp;
            else
                toExclusive = new UtcDateTime(
                    FastBinFileUtils.RoundUpToMultiple(toExclusive.Ticks, ItemTimeSpan.Ticks));

            if (fromInclusive >= FirstUnavailableTimestamp || toExclusive <= FirstTimestamp)
                return 0;

            long len = this.IndexToLong(toExclusive) - this.IndexToLong(fromInclusive);
            if (len > int.MaxValue)
                return 0;
            return (int) len;
        }

        Array IStoredUniformTimeseries.GenericReadData(UtcDateTime fromInclusive, UtcDateTime toExclusive)
        {
            return ReadData(fromInclusive, toExclusive);
        }

        Array IStoredUniformTimeseries.GenericReadData(UtcDateTime fromInclusive, int count)
        {
            long firstItemIdx = this.IndexToLong(fromInclusive);
            int bufCount = Math.Min((Count - firstItemIdx).ToIntCountChecked(), count);
            var buffer = new ArraySegment<T>(new T[bufCount], 0, bufCount);

            PerformRead(firstItemIdx, buffer);

            return buffer.Array;
        }

        void IBinUniformTimeseriesFile.GenericWriteData(UtcDateTime firstItemIndex, Array buffer, int offset, int count)
        {
            WriteData(firstItemIndex, new ArraySegment<T>((T[]) buffer, offset, count));
        }

        public void TruncateFile(UtcDateTime newFirstUnavailableTimestamp)
        {
            PerformTruncateFile(this.IndexToLong(newFirstUnavailableTimestamp));
        }

        #endregion

        private static readonly Version CurrentVersion = new Version(1, 1);
        private static readonly Version Ver10 = new Version(1, 0);

        #region IBinaryFile<T> Members

        public void ReadData(long firstItemIdx, ArraySegment<T> buffer)
        {
            PerformRead(firstItemIdx, buffer);
        }

        #endregion

        #region IStoredUniformTimeseries<T> Members

        public ITimeSeries<T> GetTimeSeries(long firstItemIdx, int count)
        {
            throw new NotImplementedException();
        }

        public int ReadData(UtcDateTime fromInclusive, ArraySegment<T> buffer)
        {
            long firstItemIdx = this.IndexToLong(fromInclusive);
            int maxCount = (Count - firstItemIdx).ToIntCountChecked();
            if (buffer.Count > maxCount)
                buffer = new ArraySegment<T>(buffer.Array, buffer.Offset, maxCount);

            PerformRead(firstItemIdx, buffer);

            return buffer.Count;
        }

        #endregion

        /// <summary>
        /// Read data starting at <paramref name="fromInclusive"/>, up to, but not including <paramref name="toExclusive"/>.
        /// </summary>
        /// <returns>The total number of items read.</returns>
        public int ReadData(UtcDateTime fromInclusive, UtcDateTime toExclusive, ArraySegment<T> buffer)
        {
            if (buffer.Array == null) throw new ArgumentNullException("buffer");
            Tuple<long, int> rng = CalcNeededBuffer(fromInclusive, toExclusive);
            PerformRead(rng.Item1, new ArraySegment<T>(buffer.Array, buffer.Offset, Math.Min(buffer.Count, rng.Item2)));
            return rng.Item2;
        }

        /// <summary>
        /// Read data starting at <paramref name="fromInclusive"/>, up to, but not including <paramref name="toExclusive"/>.
        /// </summary>
        public T[] ReadData(UtcDateTime fromInclusive, UtcDateTime toExclusive)
        {
            Tuple<long, int> rng = CalcNeededBuffer(fromInclusive, toExclusive);
            var buffer = new T[rng.Item2];

            PerformRead(rng.Item1, new ArraySegment<T>(buffer));

            return buffer;
        }

        /// <summary>
        /// Write an array of items to the file.
        /// </summary>
        /// <param name="firstItemIndex">The index of the first value in the <paramref name="buffer"/> array.</param>
        /// <param name="buffer">Array of values to be written into a file.</param>
        public void WriteData(UtcDateTime firstItemIndex, ArraySegment<T> buffer)
        {
            if (buffer.Array == null) throw new ArgumentException("buffer");

            if (!CanWrite) throw new InvalidOperationException("The file was opened as readonly");

            if (firstItemIndex < FirstTimestamp)
                throw new ArgumentOutOfRangeException("firstItemIndex", firstItemIndex,
                                                      "Must be >= FirstTimestamp (" + FirstTimestamp + ")");
            if (firstItemIndex > FirstUnavailableTimestamp)
                throw new ArgumentOutOfRangeException("firstItemIndex", firstItemIndex,
                                                      "Must be <= FirstUnavailableTimestamp (" +
                                                      FirstUnavailableTimestamp + ")");

            long itemLong = this.IndexToLong(firstItemIndex);

            PerformWrite(itemLong, buffer);
        }

        protected override void ReadCustomHeader(BinaryReader stream, Version version, IDictionary<string, Type> typeMap)
        {
            if (version == CurrentVersion || version == Ver10)
            {
                ItemTimeSpan = TimeSpan.FromTicks(stream.ReadInt64());

                // in 1.0, DateTime was serialized as binary instead of UtcDateTime.Ticks
                FirstTimestamp =
                    version == CurrentVersion
                        ? new UtcDateTime(stream.ReadInt64())
                        : new UtcDateTime(DateTime.FromBinary(stream.ReadInt64()));
            }
            else
                FastBinFileUtils.ThrowUnknownVersion(version, GetType());
        }

        protected override Version WriteCustomHeader(BinaryWriter stream)
        {
            stream.Write(ItemTimeSpan.Ticks);
            stream.Write(FirstTimestamp.Ticks);

            return CurrentVersion;
        }

        /// <summary>
        /// Returns the first index and the length of the data available in this file for the given range of dates
        /// </summary>
        protected Tuple<long, int> CalcNeededBuffer(UtcDateTime fromInclusive, UtcDateTime toExclusive)
        {
            if (fromInclusive.CompareTo(toExclusive) > 0)
                throw new ArgumentOutOfRangeException("fromInclusive", "'from' must be <= 'to'");

            long firstIndexIncl = this.IndexToLong(fromInclusive);
            return Tuple.Create(firstIndexIncl, (this.IndexToLong(toExclusive) - firstIndexIncl).ToIntCountChecked());
        }

        public ITimeSeries<T> GetTimeSeries(UtcDateTime start, UtcDateTime end)
        {
            AdjustRangeToExistingData(ref start, ref end);

            var data = ReadData(start, end);
            return new UniformTimeSeries<T>(start, ItemTimeSpan, data);
        }

        public override string ToString()
        {
            return string.Format("{0}, firstTS={1}, slice={2}", base.ToString(), FirstTimestamp, ItemTimeSpan);
        }

        ITimeSeries IHistFeedInt.GetTimeSeries(UtcDateTime start, UtcDateTime end)
        {
            return GetTimeSeries(start, end);
        }

        public long BinarySearch(UtcDateTime timestamp)
        {
            throw new NotImplementedException();
        }

        ITimeSeries IStoredTimeSeries.GetTimeSeries(long firstItemIdx, int count)
        {
            return GetTimeSeries(firstItemIdx, count);
        }
    }
}