using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CloudlogHelper.CLHProto;
using Google.Protobuf;

namespace CloudlogHelper.Utils;

public class CLHServerUtil
{
    private const string AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    
    private static uint _maxMsgLength = 10240; 
    private static Dictionary<byte, Type> _byteTypeMap = new();
    private static Dictionary<Type, byte> _typeByteMap = new();

    static CLHServerUtil()
    {
        RegisterMessage(Convert.ToByte('1'), typeof(Ping));
        RegisterMessage(Convert.ToByte('2'), typeof(Pong));
        RegisterMessage(Convert.ToByte('3'), typeof(Command));
        RegisterMessage(Convert.ToByte('4'), typeof(CommonResponse));
        RegisterMessage(Convert.ToByte('5'), typeof(CommandResponse));
        RegisterMessage(Convert.ToByte('a'), typeof(HandshakeRequest));
        RegisterMessage(Convert.ToByte('b'), typeof(HandshakeResponse));
        RegisterMessage(Convert.ToByte('c'), typeof(WsjtxMessage));
        RegisterMessage(Convert.ToByte('d'), typeof(WsjtxMessagePacked));
        RegisterMessage(Convert.ToByte('e'), typeof(RigData));
    }

    public static string GenerateRandomInstanceName(int length)
    {
        var random = new Random();
        var result = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            var index = random.Next(AllowedChars.Length);
            result.Append(AllowedChars[index]);
        }

        return $"CLH-{result}";
    }
    
    private static void RegisterMessage(byte typeByte, Type rawMsg)
    {
        if (rawMsg is null) throw new ArgumentNullException(nameof(rawMsg));
        _byteTypeMap[typeByte] = rawMsg;
        _typeByteMap[rawMsg] = typeByte;
    }
    
    private static IMessage UnpackInternal(byte typeByte, byte[] buffer, IMessage? msgIn)
    {
        IMessage msg;
        if (msgIn == null)
        {
            if (!_byteTypeMap.TryGetValue(typeByte, out var factory))
            {
                throw new Exception($"Unknown type {typeByte}");
            }
            msg = (Activator.CreateInstance(factory) as IMessage)!;
            if (msg is null)
            {
                throw new Exception($"Unknown type {typeByte}");
            }
        }
        else
        {
            msg = msgIn;
        }

        // Merge from buffer
        var cis = new CodedInputStream(buffer);
        msg.MergeFrom(cis);
        return msg;
    }
    
    public static IMessage UnPack(byte typeByte, byte[] buffer)
    {
        return UnpackInternal(typeByte, buffer, null);
    }

    public static void UnPackInto(byte[] buffer, IMessage msg)
    {
        UnpackInternal((byte)0, buffer, msg);
    }
    
    
    public static byte[] Pack(IMessage msg)
    {
        if (msg == null) throw new ArgumentNullException(nameof(msg));
        var name = msg.Descriptor.FullName;
        if (!_typeByteMap.TryGetValue(msg.GetType(), out var typeByte))
        {
            throw new Exception($"Unknown type {msg.GetType()}");
        }

        var content = msg.ToByteArray();
        var length = (uint)content.Length;

        var result = new byte[1 + 4 + content.Length];
        result[0] = typeByte;
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(1,4), length);
        Buffer.BlockCopy(content, 0, result, 5, content.Length);
        return result;
    }
    
    private static (byte typeByte, byte[] buffer) ReadMsgInternal(Stream s)
    {
        var b = s.ReadByte();
        if (b == -1) throw new EndOfStreamException();
        var typeByte = (byte)b;
        if (!_byteTypeMap.ContainsKey(typeByte)) throw new Exception($"Unknown type {typeByte}");

        var lenBuf = new byte[4];
        var read = 0;
        while (read < 4)
        {
            var n = s.Read(lenBuf, read, 4 - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
        var length = BinaryPrimitives.ReadUInt32BigEndian(lenBuf);
        if (length > _maxMsgLength) throw new Exception("Message size exceed!");

        if (length < 0) throw new Exception("Impossible"); // never true for uint, kept for parity

        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = s.Read(buffer, offset, buffer.Length - offset);
            if (n <= 0) throw new EndOfStreamException();
            offset += n;
        }

        if ((uint)offset != length) throw new Exception("Invalid message format");

        return (typeByte, buffer);
    }
    
    public static IMessage ReadMsg(Stream s)
    {
        var (typeByte, buffer) = ReadMsgInternal(s);
        return UnPack(typeByte, buffer);
    }

    public static void ReadMsgInto(Stream s, IMessage msg)
    {
        var (_, buffer) = ReadMsgInternal(s);
        UnPackInto(buffer, msg);
    }

    public static void WriteMsg(Stream s, IMessage msg)
    {
        var buffer = Pack(msg);
        s.Write(buffer, 0, buffer.Length);
    }
    
    
    /// <summary>
    /// CalcAuthKey is used for check whether key sent by client when login is correct or not.
    /// </summary>
    /// <param name="key">The key string</param>
    /// <param name="timestamp">The timestamp</param>
    /// <returns>Hex encoded SHA256 hash</returns>
    public static string CalcAuthKey(string key, long timestamp)
    {
        using var sha256 = SHA256.Create();
        
        var combined = key + timestamp;
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            
        var hex = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            hex.AppendFormat("{0:x2}", b);
        }
        return hex.ToString();
    }
}