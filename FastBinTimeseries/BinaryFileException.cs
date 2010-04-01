using System;
using System.Runtime.Serialization;
using NYurik.FastBinTimeseries.CommonCode;

namespace NYurik.FastBinTimeseries
{
    [Serializable]
    public class BinaryFileException : FormattedException
    {
        public BinaryFileException()
        {}

        public BinaryFileException(string message) : base(message)
        {}

        public BinaryFileException(string message, params object[] args) : base(message, args)
        {}

        public BinaryFileException(Exception inner, string message) : base(inner, message)
        {}

        public BinaryFileException(Exception inner, string message, params object[] args) : base(inner, message, args)
        {}

        protected BinaryFileException(SerializationInfo info, StreamingContext context) : base(info, context)
        {}
    }
}