// See the LICENSE file for license details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Ionic.Zlib
{
    // TODO: turn this into a ParallelZlibWriteBaseStream and implement all flavors

    internal class WorkItem
    {
        public byte[] inputBuffer;
        public byte[] outputBuffer;

        public Memory<byte> input;

        public int crc;
        public int index;
        public int ordinal;
        public int inputBytesAvailable;
        public int compressedBytesAvailable;
        public ZlibCodec compressor;

        public WorkItem(
            int size,
            CompressionLevel compressLevel,
            CompressionStrategy strategy,
            int ix)
        {
            inputBuffer = new byte[size];
            // alloc 5 bytes overhead for every block (with margin of safety = 2)
            int n = size + ((size / 32768) + 1) * 5 * 2;
            outputBuffer = new byte[n];

            compressor = new ZlibCodec();
            compressor.Strategy = strategy;
            compressor.InitializeDeflate(compressLevel, false);
            index = ix;
        }
    }

    /// <summary>
    ///   A class for compressing streams using the
    ///   Deflate algorithm with multiple threads.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    ///   This class performs DEFLATE compression through writing.  For
    ///   more information on the Deflate algorithm, see IETF RFC 1951,
    ///   "DEFLATE Compressed Data Format Specification version 1.3."
    /// </para>
    ///
    /// <para>
    ///   This class is similar to <see cref="DeflateStream"/>, except
    ///   that this class is for compression only, and this implementation uses an
    ///   approach that employs multiple worker threads to perform the DEFLATE.  On
    ///   a multi-cpu or multi-core computer, the performance of this class can be
    ///   significantly higher than the single-threaded DeflateStream, particularly
    ///   for larger streams.  How large?  Anything over 10mb is a good candidate
    ///   for parallel compression.
    /// </para>
    ///
    /// <para>
    ///   The tradeoff is that this class uses more memory and more CPU than the
    ///   vanilla DeflateStream, and also is less efficient as a compressor. For
    ///   large files the size of the compressed data stream can be less than 1%
    ///   larger than the size of a compressed data stream from the vanialla
    ///   DeflateStream.  For smaller files the difference can be larger.  The
    ///   difference will also be larger if you set the BufferSize to be lower than
    ///   the default value.  Your mileage may vary. Finally, for small files, the
    ///   ParallelDeflateOutputStream can be much slower than the vanilla
    ///   DeflateStream, because of the overhead associated to using the thread
    ///   pool.
    /// </para>
    ///
    /// </remarks>
    /// <seealso cref="DeflateStream" />
    public class ParallelDeflateOutputStream : Stream
    {

        private const int IO_BUFFER_SIZE_DEFAULT = 64 * 1024;
        private const int BufferPairsPerCore = 4;

        private List<WorkItem> _pool;
        private bool _leaveOpen;
        private bool emitting;
        private Stream _outStream;
        private int _maxBufferPairs;
        private int _bufferSize = IO_BUFFER_SIZE_DEFAULT;
        private AutoResetEvent _newlyCompressedBlob;
        //private ManualResetEvent _writingDone;
        //private ManualResetEvent _sessionReset;
        private bool _isClosed;
        private bool _firstWriteDone;
        private int _currentlyFilling;
        private int _lastFilled;
        private int _lastWritten;
        private int _latestCompressed;
        private Crc32 _runningCrc;
        private object _latestLock = new object();
        private Queue<int> _toWrite;
        private Queue<int> _toFill;
        private CompressionLevel _compressLevel;
        private volatile Exception? _pendingException;
        private bool _handlingException;
        private object _eLock = new object();  // protects _pendingException

#if Trace
        private object _outputLock = new object();
        private TraceBits _DesiredTrace =
            TraceBits.Session |
            TraceBits.Compress |
            TraceBits.WriteTake |
            TraceBits.WriteEnter |
            TraceBits.EmitEnter |
            TraceBits.EmitDone |
            TraceBits.EmitLock |
            TraceBits.EmitSkip |
            TraceBits.EmitBegin;
#endif

        /// <summary>
        /// Create a ParallelDeflateOutputStream.
        /// </summary>
        /// <remarks>
        ///
        /// <para>
        ///   This stream compresses data written into it via the DEFLATE
        ///   algorithm (see RFC 1951), and writes out the compressed byte stream.
        /// </para>
        ///
        /// <para>
        ///   The instance will use the default compression level, the default
        ///   buffer sizes and the default number of threads and buffers per
        ///   thread.
        /// </para>
        ///
        /// <para>
        ///   This class is similar to <see cref="DeflateStream"/>,
        ///   except that this implementation uses an approach that employs
        ///   multiple worker threads to perform the DEFLATE.  On a multi-cpu or
        ///   multi-core computer, the performance of this class can be
        ///   significantly higher than the single-threaded DeflateStream,
        ///   particularly for larger streams.  How large?  Anything over 10mb is
        ///   a good candidate for parallel compression.
        /// </para>
        ///
        /// </remarks>
        /// <param name="stream">The stream to which compressed data will be written.</param>
        public ParallelDeflateOutputStream(Stream stream)
            : this(stream, CompressionLevel.Default, CompressionStrategy.Default, false)
        {
        }

        /// <summary>
        ///   Create a ParallelDeflateOutputStream using the specified CompressionLevel.
        /// </summary>
        /// <remarks>
        ///   See the <see cref="ParallelDeflateOutputStream(Stream)"/>
        ///   constructor for example code.
        /// </remarks>
        /// <param name="stream">The stream to which compressed data will be written.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public ParallelDeflateOutputStream(Stream stream, CompressionLevel level)
            : this(stream, level, CompressionStrategy.Default, false)
        {
        }

        /// <summary>
        /// Create a ParallelDeflateOutputStream and specify whether to leave the underlying stream open
        /// when the ParallelDeflateOutputStream is closed.
        /// </summary>
        /// <remarks>
        ///   See the <see cref="ParallelDeflateOutputStream(Stream)"/>
        ///   constructor for example code.
        /// </remarks>
        /// <param name="stream">The stream to which compressed data will be written.</param>
        /// <param name="leaveOpen">
        ///    true if the application would like the stream to remain open after inflation/deflation.
        /// </param>
        public ParallelDeflateOutputStream(Stream stream, bool leaveOpen)
            : this(stream, CompressionLevel.Default, CompressionStrategy.Default, leaveOpen)
        {
        }

        /// <summary>
        /// Create a ParallelDeflateOutputStream and specify whether to leave the underlying stream open
        /// when the ParallelDeflateOutputStream is closed.
        /// </summary>
        /// <remarks>
        ///   See the <see cref="ParallelDeflateOutputStream(Stream)"/>
        ///   constructor for example code.
        /// </remarks>
        /// <param name="stream">The stream to which compressed data will be written.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        /// <param name="leaveOpen">
        ///    true if the application would like the stream to remain open after inflation/deflation.
        /// </param>
        public ParallelDeflateOutputStream(Stream stream, CompressionLevel level, bool leaveOpen)
            : this(stream, level, CompressionStrategy.Default, leaveOpen)
        {
        }

        /// <summary>
        /// Create a ParallelDeflateOutputStream using the specified
        /// CompressionLevel and CompressionStrategy, and specifying whether to
        /// leave the underlying stream open when the ParallelDeflateOutputStream is
        /// closed.
        /// </summary>
        /// <remarks>
        ///   See the <see cref="ParallelDeflateOutputStream(Stream)"/>
        ///   constructor for example code.
        /// </remarks>
        /// <param name="stream">The stream to which compressed data will be written.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        /// <param name="strategy">
        ///   By tweaking this parameter, you may be able to optimize the compression for
        ///   data with particular characteristics.
        /// </param>
        /// <param name="leaveOpen">
        ///    true if the application would like the stream to remain open after inflation/deflation.
        /// </param>
        public ParallelDeflateOutputStream(
            Stream stream,
            CompressionLevel level,
            CompressionStrategy strategy,
            bool leaveOpen)
        {
            TraceOutput(
                TraceBits.Lifecycle | TraceBits.Session,
                "-------------------------------------------------------");

            TraceOutput(TraceBits.Lifecycle | TraceBits.Session, "Create {0:X8}", GetHashCode());

            _outStream = stream;
            _compressLevel = level;
            Strategy = strategy;
            _leaveOpen = leaveOpen;
            MaxBufferPairs = 16; // default
        }


        /// <summary>
        ///   The ZLIB strategy to be used during compression.
        /// </summary>
        ///
        public CompressionStrategy Strategy
        {
            get;
            private set;
        }

        /// <summary>
        ///   The maximum number of buffer pairs to use.
        /// </summary>
        ///
        /// <remarks>
        /// <para>
        ///   This property sets an upper limit on the number of memory buffer
        ///   pairs to create.  The implementation of this stream allocates
        ///   multiple buffers to facilitate parallel compression.  As each buffer
        ///   fills up, this stream uses <see
        ///   cref="ThreadPool.QueueUserWorkItem(WaitCallback)">
        ///   ThreadPool.QueueUserWorkItem()</see>
        ///   to compress those buffers in a background threadpool thread. After a
        ///   buffer is compressed, it is re-ordered and written to the output
        ///   stream.
        /// </para>
        ///
        /// <para>
        ///   A higher number of buffer pairs enables a higher degree of
        ///   parallelism, which tends to increase the speed of compression on
        ///   multi-cpu computers.  On the other hand, a higher number of buffer
        ///   pairs also implies a larger memory consumption, more active worker
        ///   threads, and a higher cpu utilization for any compression. This
        ///   property enables the application to limit its memory consumption and
        ///   CPU utilization behavior depending on requirements.
        /// </para>
        ///
        /// <para>
        ///   For each compression "task" that occurs in parallel, there are 2
        ///   buffers allocated: one for input and one for output.  This property
        ///   sets a limit for the number of pairs.  The total amount of storage
        ///   space allocated for buffering will then be (N*S*2), where N is the
        ///   number of buffer pairs, S is the size of each buffer (<see
        ///   cref="BufferSize"/>).  By default, DotNetZip allocates 4 buffer
        ///   pairs per CPU core, so if your machine has 4 cores, and you retain
        ///   the default buffer size of 128k, then the
        ///   ParallelDeflateOutputStream will use 4 * 4 * 2 * 128kb of buffer
        ///   memory in total, or 4mb, in blocks of 128kb.  If you then set this
        ///   property to 8, then the number will be 8 * 2 * 128kb of buffer
        ///   memory, or 2mb.
        /// </para>
        ///
        /// <para>
        ///   CPU utilization will also go up with additional buffers, because a
        ///   larger number of buffer pairs allows a larger number of background
        ///   threads to compress in parallel. If you find that parallel
        ///   compression is consuming too much memory or CPU, you can adjust this
        ///   value downward.
        /// </para>
        ///
        /// <para>
        ///   The default value is 16. Different values may deliver better or
        ///   worse results, depending on your priorities and the dynamic
        ///   performance characteristics of your storage and compute resources.
        /// </para>
        ///
        /// <para>
        ///   This property is not the number of buffer pairs to use; it is an
        ///   upper limit. An illustration: Suppose you have an application that
        ///   uses the default value of this property (which is 16), and it runs
        ///   on a machine with 2 CPU cores. In that case, DotNetZip will allocate
        ///   4 buffer pairs per CPU core, for a total of 8 pairs.  The upper
        ///   limit specified by this property has no effect.
        /// </para>
        ///
        /// <para>
        ///   The application can set this value at any time, but it is effective
        ///   only before the first call to Write(), which is when the buffers are
        ///   allocated.
        /// </para>
        /// </remarks>
        public int MaxBufferPairs
        {
            get => _maxBufferPairs;
            set
            {
                if (value < 4)
                    throw new ArgumentException("Value must be 4 or greater.", nameof(value));
                _maxBufferPairs = value;
            }
        }

        /// <summary>
        ///   The size of the buffers used by the compressor threads.
        /// </summary>
        /// <remarks>
        ///
        /// <para>
        ///   The default buffer size is 128k. The application can set this value
        ///   at any time, but it is effective only before the first Write().
        /// </para>
        ///
        /// <para>
        ///   Larger buffer sizes implies larger memory consumption but allows
        ///   more efficient compression. Using smaller buffer sizes consumes less
        ///   memory but may result in less effective compression.  For example,
        ///   using the default buffer size of 128k, the compression delivered is
        ///   within 1% of the compression delivered by the single-threaded <see
        ///   cref="DeflateStream"/>.  On the other hand, using a
        ///   BufferSize of 8k can result in a compressed data stream that is 5%
        ///   larger than that delivered by the single-threaded
        ///   <c>DeflateStream</c>.  Excessively small buffer sizes can also cause
        ///   the speed of the ParallelDeflateOutputStream to drop, because of
        ///   larger thread scheduling overhead dealing with many many small
        ///   buffers.
        /// </para>
        ///
        /// <para>
        ///   The total amount of storage space allocated for buffering will be
        ///   (N*S*2), where N is the number of buffer pairs, and S is the size of
        ///   each buffer (this property). There are 2 buffers used by the
        ///   compressor, one for input and one for output.  By default, DotNetZip
        ///   allocates 4 buffer pairs per CPU core, so if your machine has 4
        ///   cores, then the number of buffer pairs used will be 16. If you
        ///   accept the default value of this property, 128k, then the
        ///   ParallelDeflateOutputStream will use 16 * 2 * 128kb of buffer memory
        ///   in total, or 4mb, in blocks of 128kb.  If you set this property to
        ///   64kb, then the number will be 16 * 2 * 64kb of buffer memory, or
        ///   2mb.
        /// </para>
        ///
        /// </remarks>
        public int BufferSize
        {
            get => _bufferSize;
            set
            {
                if (value < 1024)
                    throw new ArgumentOutOfRangeException("BufferSize",
                                                          "BufferSize must be greater than 1024 bytes");
                _bufferSize = value;
            }
        }

        /// <summary>
        /// The CRC32 for the data that was written out, prior to compression.
        /// </summary>
        /// <remarks>
        /// This value is meaningful only after a call to Close().
        /// </remarks>
        public int Crc32 { get; private set; }


        /// <summary>
        /// The total number of uncompressed bytes processed by the ParallelDeflateOutputStream.
        /// </summary>
        /// <remarks>
        /// This value is meaningful only after a call to Close().
        /// </remarks>
        public long BytesProcessed { get; private set; }


        private void InitializePoolOfWorkItems()
        {
            _toWrite = new Queue<int>();
            _toFill = new Queue<int>();
            _pool = new List<WorkItem>();
            int nTasks = BufferPairsPerCore * Environment.ProcessorCount;
            nTasks = Math.Min(nTasks, _maxBufferPairs);
            for (int i = 0; i < nTasks; i++)
            {
                _pool.Add(new WorkItem(_bufferSize, _compressLevel, Strategy, i));
                _toFill.Enqueue(i);
            }

            _newlyCompressedBlob = new AutoResetEvent(false);
            _runningCrc = new Crc32();
            _currentlyFilling = -1;
            _lastFilled = -1;
            _lastWritten = -1;
            _latestCompressed = -1;
        }




        /// <summary>
        ///   Write data to the stream.
        /// </summary>
        ///
        /// <remarks>
        ///
        /// <para>
        ///   To use the ParallelDeflateOutputStream to compress data, create a
        ///   ParallelDeflateOutputStream with CompressionMode.Compress, passing a
        ///   writable output stream.  Then call Write() on that
        ///   ParallelDeflateOutputStream, providing uncompressed data as input.  The
        ///   data sent to the output stream will be the compressed form of the data
        ///   written.
        /// </para>
        ///
        /// <para>
        ///   To decompress data, use the <see cref="DeflateStream"/> class.
        /// </para>
        ///
        /// </remarks>
        /// <param name="buffer">The buffer holding data to write to the stream.</param>
        /// <param name="offset">the offset within that data array to find the first byte to write.</param>
        /// <param name="count">the number of bytes to write.</param>

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            bool mustWait = false;

            // This method does this:
            //   0. handles any pending exceptions
            //   1. write any buffers that are ready to be written,
            //   2. fills a work buffer; when full, flip state to 'Filled',
            //   3. if more data to be written,  goto step 1

            if (_isClosed)
                throw new InvalidOperationException();

            // dispense any exceptions that occurred on the BG threads
            if (_pendingException != null)
            {
                _handlingException = true;
                var pe = _pendingException;
                _pendingException = null;
                throw pe;
            }

            if (buffer.Length == 0)
                return;

            if (!_firstWriteDone)
            {
                // Want to do this on first Write, first session, and not in the
                // constructor.  We want to allow MaxBufferPairs to
                // change after construction, but before first Write.
                InitializePoolOfWorkItems();
                _firstWriteDone = true;
            }


            do
            {
                // may need to make buffers available
                EmitPendingBuffers(false, mustWait);

                mustWait = false;
                // use current buffer, or get a new buffer to fill
                int ix = -1;
                if (_currentlyFilling >= 0)
                {
                    ix = _currentlyFilling;
                    TraceOutput(TraceBits.WriteTake,
                                "Write    notake   wi({0}) lf({1})",
                                ix,
                                _lastFilled);
                }
                else
                {
                    TraceOutput(TraceBits.WriteTake, "Write    take?");
                    if (_toFill.Count == 0)
                    {
                        // no available buffers, so... need to emit
                        // compressed buffers.
                        mustWait = true;
                        continue;
                    }

                    ix = _toFill.Dequeue();
                    TraceOutput(TraceBits.WriteTake,
                                "Write    take     wi({0}) lf({1})",
                                ix,
                                _lastFilled);
                    ++_lastFilled; // TODO: consider rollover?
                }

                WorkItem workitem = _pool[ix];

                int limit = ((workitem.inputBuffer.Length - workitem.inputBytesAvailable) > buffer.Length)
                    ? buffer.Length
                    : (workitem.inputBuffer.Length - workitem.inputBytesAvailable);

                workitem.ordinal = _lastFilled;

                TraceOutput(TraceBits.Write,
                            "Write    lock     wi({0}) ord({1}) iba({2})",
                            workitem.index,
                            workitem.ordinal,
                            workitem.inputBytesAvailable
                            );

                // copy from the provided buffer to our workitem, starting at
                // the tail end of whatever data we might have in there currently.
                buffer.Slice(0, limit).CopyTo(workitem.inputBuffer.AsSpan(workitem.inputBytesAvailable));

                buffer = buffer.Slice(limit);
                workitem.inputBytesAvailable += limit;
                workitem.input = workitem.inputBuffer.AsMemory(0, workitem.inputBytesAvailable);

                if (workitem.inputBytesAvailable == workitem.inputBuffer.Length)
                {
                    // No need for interlocked.increment: the Write()
                    // method is documented as not multi-thread safe, so
                    // we can assume Write() calls come in from only one thread.
                    TraceOutput(TraceBits.Write,
                                "Write    QUWI     wi({0}) ord({1}) iba({2}) nf({3})",
                                workitem.index,
                                workitem.ordinal,
                                workitem.inputBytesAvailable);

                    if (!ThreadPool.QueueUserWorkItem(DeflateOne, workitem))
                        throw new Exception("Cannot enqueue workitem");

                    _currentlyFilling = -1; // will get a new buffer next time
                }
                else
                    _currentlyFilling = ix;

                if (buffer.Length > 0)
                    TraceOutput(TraceBits.WriteEnter, "Write more");
            }
            while (buffer.Length > 0);  // until no more to write

            TraceOutput(TraceBits.WriteEnter, "Write exit");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }


        private void FlushFinish()
        {
            // After writing a series of compressed buffers, each one closed
            // with Flush.Sync, we now write the final one as Flush.Finish,
            // and then stop.
            Span<byte> buffer = stackalloc byte[128];
            var compressor = new ZlibCodec();
            compressor.InitializeDeflate(_compressLevel, false);

            var (code, message) = compressor.Deflate(
                ZlibFlushType.Finish, default, buffer, out _, out int written);

            if (code != ZlibCode.StreamEnd && code != ZlibCode.Ok)
                throw new Exception("deflating: " + message);

            if (written > 0)
            {
                TraceOutput(TraceBits.EmitBegin, "Emit begin flush bytes({0})", written);

                _outStream.Write(buffer.Slice(0, written));

                TraceOutput(TraceBits.EmitDone, "Emit done flush");
            }

            compressor.EndDeflate();

            Crc32 = _runningCrc.Result;
        }


        private void Flush(bool lastInput)
        {
            if (_isClosed)
                throw new InvalidOperationException();

            if (emitting)
                return;

            // compress any partial buffer
            if (_currentlyFilling >= 0)
            {
                WorkItem workitem = _pool[_currentlyFilling];
                DeflateOne(workitem);
                _currentlyFilling = -1; // get a new buffer next Write()
            }

            if (lastInput)
            {
                EmitPendingBuffers(true, false);
                FlushFinish();
            }
            else
            {
                EmitPendingBuffers(false, false);
            }
        }



        /// <summary>
        /// Flush the stream.
        /// </summary>
        public override void Flush()
        {
            if (_pendingException != null)
            {
                _handlingException = true;
                var pe = _pendingException;
                _pendingException = null;
                throw pe;
            }
            if (_handlingException)
                return;

            Flush(false);
        }

        // workitem 10030 - implement a new Dispose method

        protected override void Dispose(bool disposing)
        {
            TraceOutput(TraceBits.Lifecycle, "Dispose  {0:X8}", GetHashCode());

            if (_pendingException != null)
            {
                _handlingException = true;
                var pe = _pendingException;
                _pendingException = null;
                throw pe;
            }

            if (_handlingException)
                return;

            if (_isClosed)
                return;

            Flush(true);
            _pool = null!;

            if (disposing)
            {
                if (!_leaveOpen)
                    _outStream.Dispose();

                _newlyCompressedBlob.Dispose();
            }
            _isClosed = true;

            base.Dispose(disposing);
        }


        /// <summary>
        ///   Resets the stream for use with another stream.
        /// </summary>
        /// <remarks>
        ///   Because the ParallelDeflateOutputStream is expensive to create, it
        ///   has been designed so that it can be recycled and re-used.  You have
        ///   to call Close() on the stream first, then you can call Reset() on
        ///   it, to use it again on another stream.
        /// </remarks>
        ///
        /// <param name="stream">The new output stream for this era. </param>
        public void Reset(Stream stream)
        {
            TraceOutput(TraceBits.Session, "-------------------------------------------------------");
            TraceOutput(TraceBits.Session, "Reset {0:X8} firstDone({1})", GetHashCode(), _firstWriteDone);

            if (!_firstWriteDone)
                return;

            // reset all status
            _toWrite.Clear();
            _toFill.Clear();
            foreach (var workitem in _pool)
            {
                _toFill.Enqueue(workitem.index);
                workitem.ordinal = -1;
            }

            _firstWriteDone = false;
            BytesProcessed = 0L;
            _runningCrc = new Crc32();
            _isClosed = false;
            _currentlyFilling = -1;
            _lastFilled = -1;
            _lastWritten = -1;
            _latestCompressed = -1;
            _outStream = stream;
        }


        private void EmitPendingBuffers(bool doAll, bool mustWait)
        {
            // When combining parallel deflation with a ZipSegmentedStream, it's
            // possible for the ZSS to throw from within this method.  In that
            // case, Close/Dispose will be called on this stream, if this stream
            // is employed within a using or try/finally pair as required. But
            // this stream is unaware of the pending exception, so the Close()
            // method invokes this method AGAIN.  This can lead to a deadlock.
            // Therefore, failfast if re-entering.

            if (emitting)
                return;
            emitting = true;

            if ((doAll && (_latestCompressed != _lastFilled)) || mustWait)
            {
                _newlyCompressedBlob.WaitOne();
            }

            do
            {
                int firstSkip = -1;
                int millisecondsToWait = doAll ? 200 : (mustWait ? -1 : 0);
                int nextToWrite;
                do
                {
                    if (Monitor.TryEnter(_toWrite, millisecondsToWait))
                    {
                        nextToWrite = -1;
                        try
                        {
                            if (_toWrite.Count > 0)
                                nextToWrite = _toWrite.Dequeue();
                        }
                        finally
                        {
                            Monitor.Exit(_toWrite);
                        }

                        if (nextToWrite >= 0)
                        {
                            WorkItem workitem = _pool[nextToWrite];
                            if (workitem.ordinal != _lastWritten + 1)
                            {
                                // out of order. requeue and try again.
                                TraceOutput(TraceBits.EmitSkip,
                                            "Emit     skip     wi({0}) ord({1}) lw({2}) fs({3})",
                                            workitem.index,
                                            workitem.ordinal,
                                            _lastWritten,
                                            firstSkip);

                                lock (_toWrite)
                                {
                                    _toWrite.Enqueue(nextToWrite);
                                }

                                if (firstSkip == nextToWrite)
                                {
                                    // We went around the list once.
                                    // None of the items in the list is the one we want.
                                    // Now wait for a compressor to signal again.
                                    _newlyCompressedBlob.WaitOne();
                                    firstSkip = -1;
                                }
                                else if (firstSkip == -1)
                                    firstSkip = nextToWrite;

                                continue;
                            }

                            firstSkip = -1;

                            TraceOutput(TraceBits.EmitBegin,
                                        "Emit     begin    wi({0}) ord({1})              cba({2})",
                                        workitem.index,
                                        workitem.ordinal,
                                        workitem.compressedBytesAvailable);

                            _outStream.Write(workitem.outputBuffer, 0, workitem.compressedBytesAvailable);

                            _runningCrc.Combine(workitem.crc, workitem.inputBytesAvailable);

                            BytesProcessed += workitem.inputBytesAvailable;
                            workitem.inputBytesAvailable = 0;

                            TraceOutput(TraceBits.EmitDone,
                                        "Emit     done     wi({0}) ord({1})              cba({2}) mtw({3})",
                                        workitem.index,
                                        workitem.ordinal,
                                        workitem.compressedBytesAvailable,
                                        millisecondsToWait);

                            _lastWritten = workitem.ordinal;
                            _toFill.Enqueue(workitem.index);

                            // don't wait next time through
                            if (millisecondsToWait == -1)
                                millisecondsToWait = 0;
                        }
                    }
                    else
                        nextToWrite = -1;

                } while (nextToWrite >= 0);

            } while (doAll && (_lastWritten != _latestCompressed || _lastWritten != _lastFilled));

            emitting = false;
        }

        private void DeflateOne(object? wi)
        {
            if (wi == null)
                throw new ArgumentNullException(nameof(wi));

            // compress one buffer
            var workitem = (WorkItem)wi;
            try
            {
                int myItem = workitem.index;
                var crc = new Crc32();

                // calc CRC on the buffer
                crc.Slurp(workitem.input.Span);

                // deflate it
                DeflateOneSegment(workitem);

                // update status
                workitem.crc = crc.Result;

                TraceOutput(TraceBits.Compress,
                            "Compress          wi({0}) ord({1}) len({2})",
                            workitem.index,
                            workitem.ordinal,
                            workitem.compressedBytesAvailable);

                lock (_latestLock)
                {
                    if (workitem.ordinal > _latestCompressed)
                        _latestCompressed = workitem.ordinal;
                }
                lock (_toWrite)
                {
                    _toWrite.Enqueue(workitem.index);
                }
                _newlyCompressedBlob.Set();
            }
            catch (Exception exc1)
            {
                lock (_eLock)
                {
                    // expose the exception to the main thread
                    if (_pendingException != null)
                        _pendingException = exc1;
                }
            }
        }

        private static void DeflateOneSegment(WorkItem workitem)
        {
            ZlibCodec compressor = workitem.compressor;
            compressor.ResetDeflate();

            ref var input = ref workitem.input;
            var output = workitem.outputBuffer.AsSpan();

            ZlibCode code;
            string? message;

            int consumed;
            int written;
            workitem.compressedBytesAvailable = 0;

            // step 1: deflate the buffer
            do
            {
                (code, message) = compressor.Deflate(
                    ZlibFlushType.None, input.Span, output, out consumed, out written);

                if (code != ZlibCode.StreamEnd && code != ZlibCode.Ok)
                    throw new Exception("deflating: " + message);

                input = input.Slice(consumed);
                output = output.Slice(written);
                workitem.compressedBytesAvailable += written;
            }
            while (input.Length > 0 || output.Length == 0);

            // step 2: flush (sync)
            (code, message) = compressor.Deflate(
                ZlibFlushType.Sync, input.Span, output, out consumed, out written);

            if (code != ZlibCode.StreamEnd && code != ZlibCode.Ok)
                throw new Exception("deflating: " + message);

            input = input.Slice(consumed);
            workitem.compressedBytesAvailable += written;
        }


        [System.Diagnostics.Conditional("Trace")]
        private static void TraceOutput(TraceBits bits, string format, params object[] varParams)
        {
#if Trace
            if ((bits & _DesiredTrace) != 0)
            {
                lock (_outputLock)
                {
                    int tid = Thread.CurrentThread.GetHashCode();

                    Console.ForegroundColor = (ConsoleColor)(tid % 8 + 8);
                    Console.Write("{0:000} PDOS ", tid);
                    Console.WriteLine(format, varParams);
                    Console.ResetColor();
                }
            }
#endif
        }


        // used only when Trace is defined
        [Flags]
        private enum TraceBits : uint
        {
            None = 0,
            NotUsed1 = 1,
            EmitLock = 2,
            EmitEnter = 4,      // enter _EmitPending
            EmitBegin = 8,      // begin to write out
            EmitDone = 16,      // done writing out
            EmitSkip = 32,      // writer skipping a workitem
            EmitAll = 58,       // All Emit flags
            Flush = 64,
            Lifecycle = 128,    // constructor/disposer
            Session = 256,      // Close/Reset
            Synch = 512,        // thread synchronization
            Instance = 1024,    // instance settings
            Compress = 2048,    // compress task
            Write = 4096,       // filling buffers, when caller invokes Write()
            WriteEnter = 8192,  // upon entry to Write()
            WriteTake = 16384,  // on _toFill.Take()
            All = 0xffffffff,
        }



        /// <summary>
        /// Indicates whether the stream supports Seek operations.
        /// </summary>
        /// <remarks>
        /// Always returns false.
        /// </remarks>
        public override bool CanSeek => false;


        /// <summary>
        /// Indicates whether the stream supports Read operations.
        /// </summary>
        /// <remarks>
        /// Always returns false.
        /// </remarks>
        public override bool CanRead => false;

        /// <summary>
        /// Indicates whether the stream supports Write operations.
        /// </summary>
        /// <remarks>
        /// Returns true if the provided stream is writable.
        /// </remarks>
        public override bool CanWrite => _outStream.CanWrite;

        /// <summary>
        /// Reading this property always throws a NotSupportedException.
        /// </summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        /// Returns the current position of the output stream.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Because the output gets written by a background thread,
        ///     the value may change asynchronously.  Setting this
        ///     property always throws a NotSupportedException.
        ///   </para>
        /// </remarks>
        public override long Position
        {
            get => _outStream.Position;
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override int Read(Span<byte> buffer)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

    }

}

