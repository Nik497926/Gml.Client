using System;
using System.Collections.Generic;

namespace Gml.Client.Models;

public class UnicorePlayerCabinetDto
{
    public bool Available { get; set; }
    public string? Message { get; set; }
    public decimal? Balance { get; set; }
    public string? CabinetUrl { get; set; }
    public long TotalPlaytime { get; set; }
    public List<UnicorePlayerServerDto> Servers { get; set; } = new();
}

public class UnicorePlayerServerDto
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public long Playtime { get; set; }
    public List<UnicoreDonateGroupDto> DonateGroups { get; set; } = new();
}

public class UnicoreDonateGroupDto
{
    public string Name { get; set; } = string.Empty;
    public string? IngameId { get; set; }
    public DateTime? Expired { get; set; }
}
