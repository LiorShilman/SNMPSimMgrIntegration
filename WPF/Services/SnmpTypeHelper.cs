using SnmpSharpNet;

namespace SNMPSimMgr.Services;

/// <summary>
/// SNMP ASN.1 type byte constants and conversion helpers.
/// SnmpSharpNet's SnmpConstants.SMI_* fields are static readonly (not const),
/// so we define our own const values for use in switch expressions.
/// </summary>
internal static class SnmpTypeHelper
{
    // ASN.1 type byte values
    public const byte Integer32 = 0x02;
    public const byte OctetString = 0x04;
    public const byte Null = 0x05;
    public const byte ObjectId = 0x06;
    public const byte IpAddress = 0x40;
    public const byte Counter32 = 0x41;
    public const byte Gauge32 = 0x42;
    public const byte TimeTicks = 0x43;
    public const byte Opaque = 0x44;
    public const byte Counter64 = 0x46;
    public const byte NoSuchObject = 0x80;
    public const byte NoSuchInstance = 0x81;
    public const byte EndOfMibView = 0x82;

    public static string TypeToString(byte type) => type switch
    {
        Integer32 => "Integer32",
        OctetString => "OctetString",
        ObjectId => "ObjectIdentifier",
        IpAddress => "IpAddress",
        Counter32 => "Counter32",
        Gauge32 => "Gauge32",
        TimeTicks => "TimeTicks",
        Counter64 => "Counter64",
        _ => $"Type_{type}"
    };

    public static byte StringToType(string typeName) => typeName switch
    {
        "Integer32" => Integer32,
        "OctetString" => OctetString,
        "ObjectIdentifier" => ObjectId,
        "IpAddress" => IpAddress,
        "Counter32" => Counter32,
        "Gauge32" => Gauge32,
        "TimeTicks" => TimeTicks,
        "Counter64" => Counter64,
        _ => OctetString,
    };

    public static AsnType CreateValue(byte type, string value)
    {
        try
        {
            return type switch
            {
                Integer32 => new Integer32(int.TryParse(value, out var i) ? i : 0),
                Counter32 => new Counter32(uint.TryParse(value, out var c) ? c : 0),
                Gauge32 => new Gauge32(uint.TryParse(value, out var g) ? g : 0),
                TimeTicks => new TimeTicks(uint.TryParse(value, out var t) ? t : 0),
                Counter64 => new Counter64(ulong.TryParse(value, out var c64) ? c64 : 0),
                ObjectId => new Oid(value),
                IpAddress => new SnmpSharpNet.IpAddress(value),
                _ => new SnmpSharpNet.OctetString(value),
            };
        }
        catch
        {
            return new SnmpSharpNet.OctetString(value);
        }
    }
}
