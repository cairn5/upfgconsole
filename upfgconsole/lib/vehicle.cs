namespace lib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Mail;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security;
using ScottPlot.LayoutEngines;
using ScottPlot.Rendering.RenderActions;


public class Stage
{
    public int Id { get; set; }
    public int Mode { get; set; }
    public int GLim { get; set; }
    public double MassTotal { get; set; }
    public double MassFuel { get; set; }
    public double Thrust { get; set; }
    public double Isp { get; set; }

}

// Placeholder classes for Vehicle and Environment
public class Vehicle
{
    public List<Stage> Stages { get; set; } = new List<Stage>();
    
    public static Vehicle FromJson(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Vehicle configuration file not found: {filePath}");

        string json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var vehicle = JsonSerializer.Deserialize<Vehicle>(json, options);
        if (vehicle == null)
            throw new InvalidOperationException("Failed to deserialize the vehicle configuration.");

        return vehicle;
    }
}
