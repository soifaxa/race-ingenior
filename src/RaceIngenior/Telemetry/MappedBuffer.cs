using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using RaceIngenior.Telemetry.rFactor2Data;

namespace RaceIngenior.Telemetry;

/// <summary>
/// Read helper adapted from The Iron Wolf rF2SMMonitor MappedBuffer.
/// </summary>
public sealed class MappedBuffer<T> where T : struct
{
    private const int MaxRetries = 10;

    private readonly int _bufferSizeBytes = Marshal.SizeOf<T>();
    private readonly int _versionBlockSize = Marshal.SizeOf<rF2MappedBufferVersionBlock>();
    private readonly int _versionBlockWithSize = Marshal.SizeOf<rF2MappedBufferVersionBlockWithSize>();
    private readonly string _bufferName;
    private readonly bool _partial;
    private readonly bool _skipUnchanged;

    private MemoryMappedFile? _mmf;
    private byte[]? _fullSizeBuffer;
    private uint _lastBegin;
    private uint _lastEnd;

    public bool IsConnected => _mmf is not null;

    public MappedBuffer(string bufferName, bool partial = true, bool skipUnchanged = true)
    {
        _bufferName = bufferName;
        _partial = partial;
        _skipUnchanged = skipUnchanged;
    }

    public bool TryConnect()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(_bufferName);
            _fullSizeBuffer = new byte[_bufferSizeBytes];
            return true;
        }
        catch (FileNotFoundException)
        {
            Disconnect();
            return false;
        }
        catch (IOException)
        {
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _mmf?.Dispose();
        _mmf = null;
        _fullSizeBuffer = null;
    }

    public bool TryRead(ref T mappedData)
    {
        if (_mmf is null || _fullSizeBuffer is null)
            return false;

        using var view = _mmf.CreateViewStream();
        var reader = new BinaryReader(view);

        for (var retry = 0; retry < MaxRetries; retry++)
        {
            var bufferSize = _bufferSizeBytes;
            uint begin;
            uint end;

            if (_partial)
            {
                var header = ReadHeader<rF2MappedBufferVersionBlockWithSize>(reader, _versionBlockWithSize);
                begin = header.mVersionUpdateBegin;
                end = header.mVersionUpdateEnd;
                if (header.mBytesUpdatedHint != 0)
                    bufferSize = header.mBytesUpdatedHint;
            }
            else
            {
                var header = ReadHeader<rF2MappedBufferVersionBlock>(reader, _versionBlockSize);
                begin = header.mVersionUpdateBegin;
                end = header.mVersionUpdateEnd;
            }

            if (_skipUnchanged && begin == _lastBegin && end == _lastEnd)
                return true;

            if (begin != end)
            {
                Thread.Sleep(1);
                continue;
            }

            view.Position = 0;
            var bytes = reader.ReadBytes(bufferSize);
            if (bytes.Length < _versionBlockSize)
                return false;

            var version = BytesToStruct<rF2MappedBufferVersionBlock>(bytes);
            if (version.mVersionUpdateBegin != version.mVersionUpdateEnd)
            {
                Thread.Sleep(1);
                continue;
            }

            var post = ReadHeader<rF2MappedBufferVersionBlock>(reader, _versionBlockSize);
            if (version.mVersionUpdateBegin != post.mVersionUpdateBegin
                || version.mVersionUpdateEnd != post.mVersionUpdateEnd)
            {
                Thread.Sleep(1);
                continue;
            }

            if (_partial)
            {
                Array.Copy(bytes, _fullSizeBuffer, bytes.Length);
                mappedData = BytesToStruct<T>(_fullSizeBuffer);
            }
            else
            {
                mappedData = BytesToStruct<T>(bytes);
            }

            _lastBegin = begin;
            _lastEnd = end;
            return true;
        }

        return false;
    }

    private static THeader ReadHeader<THeader>(BinaryReader reader, int size) where THeader : struct
    {
        reader.BaseStream.Position = 0;
        var bytes = reader.ReadBytes(size);
        return BytesToStruct<THeader>(bytes);
    }

    private static TStruct BytesToStruct<TStruct>(byte[] bytes) where TStruct : struct
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<TStruct>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
}
