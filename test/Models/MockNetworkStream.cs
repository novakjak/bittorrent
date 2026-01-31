using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace test.Models;

public class MockNetworkStream : Stream, IDisposable
{
    private readonly MemoryStream _to = new();
    private long _toRead = 0;
    private readonly SemaphoreSlim _toSemaphore = new(0);
    private readonly MemoryStream _from = new();
    private long _fromRead = 0;
    private readonly SemaphoreSlim _fromSemaphore = new(0);
    private bool isClosed = false;

    public override bool CanSeek { get => false; }
    public override bool CanRead { get => true; }
    public override bool CanWrite { get => true; }
    public override bool CanTimeout { get => true; }
    public override long Position { get => 0; set => _ = value; }
    public override long Length { get => Math.Max(_to.Length, _from.Length); }
    public override int ReadTimeout { get; set; } = 10000;

    public MockNetworkStream() { }

    public override int Read(byte[] buffer, int start, int count)
    {
        if (isClosed)
        {
            throw new ObjectDisposedException(this.ToString());
        }
        if (_fromRead == _from.Length && !_fromSemaphore.Wait(1000))
            throw new TimeoutException();
        _from.Seek(_fromRead, SeekOrigin.Begin);
        var res = _from.Read(buffer, start, count);
        _fromRead += res;
        if (_fromRead == _from.Length && _fromSemaphore.CurrentCount > 0)
            _fromSemaphore.Wait(ReadTimeout);
        return res;
    }
    public override void Write(byte[] buffer, int start, int count)
    {
        if (isClosed)
        {
            throw new ObjectDisposedException(this.ToString());
        }
        _to.Seek(0, SeekOrigin.End);
        _to.Write(buffer, start, count);
        if (count > 0 && _toSemaphore.CurrentCount == 0)
            _toSemaphore.Release();
    }
    public override void Flush()
    {
        if (isClosed)
        {
            throw new ObjectDisposedException(this.ToString());
        }
        _to.Flush();
        _from.Flush();
    }
    public override long Seek(long position, SeekOrigin origin) => throw new NotImplementedException();

    public override async Task<int> ReadAsync(byte[] buffer, int start, int count, CancellationToken token)
    {
        if (isClosed)
        {
            throw new ObjectDisposedException(this.ToString());
        }
        if (_toRead == _to.Length && !await _toSemaphore.WaitAsync(ReadTimeout, token))
            throw new TimeoutException();
        _to.Seek(_toRead, SeekOrigin.Begin);
        var res = await _to.ReadAsync(buffer, start, count, token);
        _toRead += res;
        if (_toRead == _to.Length && _toSemaphore.CurrentCount > 0)
            await _toSemaphore.WaitAsync(token);

        return res;
    }
    public override async Task WriteAsync(byte[] buffer, int start, int count, CancellationToken token)
    {
        if (isClosed)
        {
            throw new ObjectDisposedException(this.ToString());
        }
        _from.Seek(0, SeekOrigin.End);
        await _from.WriteAsync(buffer, start, count, token);
        if (_fromSemaphore.CurrentCount == 0)
            _fromSemaphore.Release();
    }

    public override async Task FlushAsync(CancellationToken token)
    {
        if (isClosed)
        {
            throw new ObjectDisposedException(this.ToString());
        }
        await _to.FlushAsync(token);
        await _from.FlushAsync(token);
    }

    public override void Close()
    {
        isClosed = true;
        base.Close();
        _to.Close();
        _from.Close();
    }

    public new void Dispose()
    {
        isClosed = true;
        base.Dispose();
        _to.Dispose();
        _from.Dispose();
    }

    public override void SetLength(long len) => throw new NotImplementedException();
}
