using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace SNMPSimMgr.Models
{
    public static class SnmpVersionOptionValues
    {
        public static SnmpVersionOption[] All => (SnmpVersionOption[])Enum.GetValues(typeof(SnmpVersionOption));
    }

    public static class AuthProtocolValues
    {
        public static AuthProtocol[] All => (AuthProtocol[])Enum.GetValues(typeof(AuthProtocol));
    }

    public static class PrivProtocolValues
    {
        public static PrivProtocol[] All => (PrivProtocol[])Enum.GetValues(typeof(PrivProtocol));
    }

    public static class SnmpTypeNames
    {
        public static string[] All => new[]
        {
            "Integer32", "OctetString", "ObjectIdentifier", "IpAddress",
            "Counter32", "Gauge32", "TimeTicks", "Counter64"
        };
    }
}
