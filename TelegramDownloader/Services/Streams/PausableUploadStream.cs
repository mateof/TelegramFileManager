using TelegramDownloader.Models;

namespace TelegramDownloader.Services.Streams
{
    /// <summary>
    /// The source of an upload, which stops giving data while the upload is
    /// paused. The Telegram client reads the file part by part as it sends it,
    /// so this pauses the upload with nothing lost: the parts already sent stay
    /// on Telegram's side and the next ones go on from there.
    /// </summary>
    public sealed class PausableUploadStream : Stream
    {
        private readonly Stream _inner;
        private readonly UploadModel _upload;

        public PausableUploadStream(Stream inner, UploadModel upload)
        {
            _inner = inner;
            _upload = upload;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _upload.WaitWhilePausedAsync().GetAwaiter().GetResult();
            return _inner.Read(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _upload.WaitWhilePausedAsync(cancellationToken);
            return await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _upload.WaitWhilePausedAsync(cancellationToken);
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
