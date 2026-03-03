using System;
using System.Text.Json.Serialization;

namespace SNMPSimMgr.Models;

public class SnmpRecord
{
    public string Oid { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
}

public class QueryResultItem
{
    public string Time { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Oid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
}

public class TrafficRecord
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TrafficDirection Direction { get; set; }
    public SnmpOperation Operation { get; set; }
    public string Oid { get; set; } = string.Empty;
    public string? RequestValue { get; set; }
    public string? ResponseValue { get; set; }
    public string? ResponseType { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrafficDirection { Request, Response }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SnmpOperation { Get, GetNext, GetBulk, Set, Walk }
