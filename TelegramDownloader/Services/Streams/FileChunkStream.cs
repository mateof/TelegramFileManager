using System;
using System.IO;

namespace TelegramDownloader.Services.Streams
{
    /// <summary>
    /// A read-only stream that provides access to a specific chunk of a file.
    /// This allows uploading large files in memory-efficient chunks without
    /// creating temporary split files on disk.
    /// </summary>
    public class FileChunkStream : Stream
    {
        private readonly FileStream _sourceStream;
        private readonly long _chunkStart;
        private readonly long _chunkLength;
        private long _position;
        private bool _disposed;

        /// <summary>
        /// Creates a new FileChunkStream for a specific portion of a file.
        /// </summary>
        /// <param name="filePath">Path to the source file</param>
        /// <param name="chunkStart">Starting byte position of the chunk</param>
        /// <param name="chunkLength">Length of the chunk in bytes</param>
        public FileChunkStream(string filePath, long chunkStart, long chunkLength)
        {
            _sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _chunkStart = chunkStart;
            _chunkLength = chunkLength;
            _position = 0;

            // Validate parameters
            if (chunkStart < 0 || chunkStart >= _sourceStream.Length)
                throw new ArgumentOutOfRangeException(nameof(chunkStart), "Chunk start is out of file bounds");

            // Adjust chunk length if it exceeds file end
            if (chunkStart + chunkLength > _sourceStream.Length)
                _chunkLength = _sourceStream.Length - chunkStart;

            // Position source stream at chunk start
            _sourceStream.Seek(_chunkStart, SeekOrigin.Begin);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _chunkLength;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _chunkLength)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
                _sourceStream.Seek(_chunkStart + _position, SeekOrigin.Begin);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileChunkStream));

            // Calculate how many bytes we can read
            long remainingInChunk = _chunkLength - _position;
            if (remainingInChunk <= 0)
                return 0;

            int bytesToRead = (int)Math.Min(count, remainingInChunk);
            int bytesRead = _sourceStream.Read(buffer, offset, bytesToRead);
            _position += bytesRead;
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileChunkStream));

            // Calculate how many bytes we can read
            long remainingInChunk = _chunkLength - _position;
            if (remainingInChunk <= 0)
                return 0;

            int bytesToRead = (int)Math.Min(count, remainingInChunk);
            int bytesRead = await _sourceStream.ReadAsync(buffer, offset, bytesToRead, cancellationToken);
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _chunkLength + offset,
                _ => throw new ArgumentException("Invalid SeekOrigin", nameof(origin))
            };

            if (newPosition < 0 || newPosition > _chunkLength)
                throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is out of chunk bounds");

            _position = newPosition;
            _sourceStream.Seek(_chunkStart + _position, SeekOrigin.Begin);
            return _position;
        }

        public override void Flush()
        {
            // Read-only stream, nothing to flush
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("FileChunkStream is read-only");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("FileChunkStream is read-only");
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _sourceStream?.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
