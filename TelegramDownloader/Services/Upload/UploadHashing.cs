using System.IO.Hashing;

namespace TelegramDownloader.Services.Upload
{
    /// <summary>
    /// The content hash stored in <c>BsonFileManagerModel.XXHash</c> when
    /// <c>CheckHash</c> is enabled.
    /// </summary>
    /// <remarks>
    /// It is XxHash64 (seed 0) rendered as uppercase hex without separators,
    /// computed the way the upload always did: the file is read in 512-byte
    /// blocks and every block is appended whole, so when the length is not a
    /// multiple of 512 the last block carries, after the real bytes, the stale
    /// tail of the previous block (or zeros for files shorter than 512 bytes).
    /// That is not the plain XxHash64 of the file, but it is what the channel
    /// indexes already hold, so every path that produces or compares this value
    /// must keep it. <see cref="ComputeStream"/> is that original algorithm,
    /// kept as the reference; <see cref="UploadHashAccumulator"/> produces the
    /// same value while the file is being uploaded.
    /// </remarks>
    public static class StoredXxHash
    {
        /// <summary>Block size of the original two-pass loop.</summary>
        public const int BlockSize = 512;

        /// <summary>Format of the stored value: uppercase hex, no separators.</summary>
        public static string Format(byte[] hash) => BitConverter.ToString(hash).Replace("-", string.Empty);

        /// <summary>
        /// The original two-pass computation, unchanged: read the whole stream
        /// in 512-byte blocks and append each block whole.
        /// </summary>
        public static string ComputeStream(Stream stream)
        {
            var hashAlgorithm = new XxHash64();
            var buffer = new Span<byte>(new byte[BlockSize]);
            while (stream.Read(buffer) > 0)
            {
                hashAlgorithm.Append(buffer);
            }
            return Format(hashAlgorithm.GetHashAndReset());
        }

        /// <summary>Stored hash of a file on disk (reads the whole file).</summary>
        public static string ComputeFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return ComputeStream(stream);
        }

        /// <summary>
        /// Stored hash of a file read from disk with a large buffer; same value
        /// as <see cref="ComputeFile"/>, used where only the value matters
        /// (duplicate detection in the upload preview).
        /// </summary>
        public static async Task<string> ComputeFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var length = new FileInfo(filePath).Length;
            var acc = new UploadHashAccumulator(length);
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 20];
            long offset = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                acc.Observe(offset, buffer.AsSpan(0, read));
                offset += read;
            }
            return acc.Complete(stream);
        }
    }

    /// <summary>
    /// Computes the stored XXHash of a file from the bytes the upload already
    /// reads, so <c>CheckHash</c> no longer costs a full extra read of every
    /// file before it is sent.
    /// </summary>
    /// <remarks>
    /// Bytes are reported with their absolute offset in the file. Only the
    /// next contiguous bytes are hashed (a high-water mark), so:
    /// <list type="bullet">
    /// <item>a retry that restarts a stream from the beginning re-reads bytes
    /// that are already hashed: they are ignored, nothing is hashed twice;</item>
    /// <item>split uploads report each part with its offset in the file
    /// (memory chunks, or the disk split pass), parts in order append
    /// seamlessly;</item>
    /// <item>bytes past a gap (a seek forward, a reused split part that was not
    /// read) are ignored, and <see cref="Complete"/> reads whatever was not
    /// seen from the file itself.</item>
    /// </list>
    /// The result is therefore always equal to
    /// <see cref="StoredXxHash.ComputeStream"/> over the whole file.
    /// Thread-safe.
    /// </remarks>
    public sealed class UploadHashAccumulator
    {
        private readonly object _lock = new();
        private readonly XxHash64 _hash = new();
        private long _hashedUpTo;
        private bool _completed;

        /// <param name="length">Length of the whole file.</param>
        public UploadHashAccumulator(long length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Length = length;
        }

        /// <summary>Length of the whole file.</summary>
        public long Length { get; }

        /// <summary>Bytes hashed so far, from the start of the file.</summary>
        public long HashedBytes { get { lock (_lock) return _hashedUpTo; } }

        /// <summary>Bytes <see cref="Complete"/> had to read itself (0 when the upload covered the whole file).</summary>
        public long CatchUpBytes { get; private set; }

        /// <summary>Reports bytes read at absolute <paramref name="offset"/> of the file.</summary>
        public void Observe(long offset, ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty || offset < 0) return;
            lock (_lock)
            {
                if (_completed) return;
                long end = offset + data.Length;
                if (offset > _hashedUpTo || end <= _hashedUpTo)
                    return; // gap ahead, or already hashed (a retry re-reading)
                if (end > Length)
                    end = Length; // never hash past the file length
                if (end <= _hashedUpTo)
                    return;
                int skip = (int)(_hashedUpTo - offset);
                _hash.Append(data.Slice(skip, (int)(end - _hashedUpTo)));
                _hashedUpTo = end;
            }
        }

        /// <summary>
        /// Finishes the hash: reads from <paramref name="source"/> (the whole
        /// file, seekable) the bytes the upload did not report, then appends the
        /// stale tail of the original 512-byte loop. Returns the stored format.
        /// </summary>
        public string Complete(Stream source)
        {
            lock (_lock)
            {
                if (_completed) throw new InvalidOperationException("The hash was already completed.");
                _completed = true;

                if (_hashedUpTo < Length)
                {
                    source.Seek(_hashedUpTo, SeekOrigin.Begin);
                    var buffer = new byte[1 << 16];
                    while (_hashedUpTo < Length)
                    {
                        int want = (int)Math.Min(buffer.Length, Length - _hashedUpTo);
                        int read = source.Read(buffer, 0, want);
                        if (read <= 0)
                            throw new EndOfStreamException("The file is shorter than when the upload started.");
                        _hash.Append(buffer.AsSpan(0, read));
                        _hashedUpTo += read;
                        CatchUpBytes += read;
                    }
                }

                int remainder = (int)(Length % StoredXxHash.BlockSize);
                if (remainder != 0)
                {
                    // The original loop appended the whole last block: its real bytes,
                    // then what the buffer still held from the previous block, i.e.
                    // bytes [Length - 512, Length - remainder) of the file, or zeros
                    // when the file has no previous block.
                    var pad = new byte[StoredXxHash.BlockSize - remainder];
                    if (Length >= StoredXxHash.BlockSize)
                    {
                        source.Seek(Length - StoredXxHash.BlockSize, SeekOrigin.Begin);
                        source.ReadExactly(pad);
                    }
                    _hash.Append(pad);
                }

                return StoredXxHash.Format(_hash.GetHashAndReset());
            }
        }
    }

    /// <summary>
    /// Read-only pass-through stream that reports every byte read to an
    /// <see cref="UploadHashAccumulator"/>, with its absolute offset in the
    /// file (<c>baseOffset</c> + position in this stream). Wraps the stream
    /// given to the Telegram upload.
    /// </summary>
    public sealed class HashingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly UploadHashAccumulator _accumulator;
        private readonly long _baseOffset;
        private readonly bool _leaveOpen;
        private long _position;

        /// <param name="inner">Stream actually read (the file, or a chunk of it).</param>
        /// <param name="accumulator">Hash of the whole file.</param>
        /// <param name="baseOffset">Offset in the file of the first byte of <paramref name="inner"/>.</param>
        /// <param name="leaveOpen">Do not dispose <paramref name="inner"/> with this stream.</param>
        public HashingReadStream(Stream inner, UploadHashAccumulator accumulator, long baseOffset = 0, bool leaveOpen = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _accumulator = accumulator ?? throw new ArgumentNullException(nameof(accumulator));
            _baseOffset = baseOffset;
            _leaveOpen = leaveOpen;
            _position = inner.CanSeek ? inner.Position : 0;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _position;
            set { _inner.Position = value; _position = value; }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read = _inner.Read(buffer);
            Report(buffer.Slice(0, read));
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Report(buffer.Span.Slice(0, read));
            return read;
        }

        private void Report(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return;
            _accumulator.Observe(_baseOffset + _position, data);
            _position += data.Length;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = _inner.Seek(offset, origin);
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_leaveOpen)
                await _inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
