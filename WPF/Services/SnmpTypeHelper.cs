using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SnmpSharpNet;

namespace SNMPSimMgr.Services
{
    /// <summary>
    /// SNMP ASN.1 type byte constants and conversion helpers.
    /// SnmpSharpNet's SnmpConstants.SMI_* fields are static readonly (not const),
    /// so we define our own const values for use in switch statements.
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

        public static string TypeToString(byte type)
        {
            switch (type)
            {
                case Integer32: return "Integer32";
                case OctetString: return "OctetString";
                case ObjectId: return "ObjectIdentifier";
                case IpAddress: return "IpAddress";
                case Counter32: return "Counter32";
                case Gauge32: return "Gauge32";
                case TimeTicks: return "TimeTicks";
                case Counter64: return "Counter64";
                default: return $"Type_{type}";
            }
        }

        public static byte StringToType(string typeName)
        {
            switch (typeName)
            {
                case "Integer32": return Integer32;
                case "OctetString": return OctetString;
                case "ObjectIdentifier": return ObjectId;
                case "IpAddress": return IpAddress;
                case "Counter32": return Counter32;
                case "Gauge32": return Gauge32;
                case "TimeTicks": return TimeTicks;
                case "Counter64": return Counter64;
                default: return OctetString;
            }
        }

        public static AsnType CreateValue(byte type, string value)
        {
            try
            {
                switch (type)
                {
                    case Integer32:
                        return new Integer32(int.TryParse(value, out var i) ? i : 0);
                    case Counter32:
                        return new Counter32(uint.TryParse(value, out var c) ? c : 0);
                    case Gauge32:
                        return new Gauge32(uint.TryParse(value, out var g) ? g : 0);
                    case TimeTicks:
                        return new TimeTicks(uint.TryParse(value, out var t) ? t : 0);
                    case Counter64:
                        return new Counter64(ulong.TryParse(value, out var c64) ? c64 : 0);
                    case ObjectId:
                        return new Oid(value);
                    case IpAddress:
                        return new SnmpSharpNet.IpAddress(value);
                    default:
                        return new SnmpSharpNet.OctetString(value);
                }
            }
            catch
            {
                return new SnmpSharpNet.OctetString(value);
            }
        }
    }
}
