using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using lib;
using NuGet.Frameworks;
namespace lib_tests;

public class UpfgTests
{
    [Fact]
    public void setup_returns_expected_rd()
    {
        float lat = 28.5f;
        string path = "/home/oli/code/csharp/upfgconsole/upfgconsole/saturnV.json";

        MissionConfig mission = Utils.ReadMission(path);
        Vehicle veh = Vehicle.FromStages(mission);
        Dictionary<string, float> desOrbit = mission.Orbit;

        double azimuth = Math.Asin(Math.Cos(Utils.DegToRad(desOrbit["inc"])) / Math.Cos(Utils.DegToRad(lat)));

        var initial = new Dictionary<string, double>
        {
            {"altitude", 45 },
            {"fpa", 50 },
            {"speed", 2400 },
            {"latitude", lat},
            {"longitude", 0 },
            {"heading", Utils.RadToDeg(azimuth) }
        };

        Simulator sim = new Simulator();
        sim.SetVesselStateFromLatLong(initial);
        sim.SetVehicle(veh);
        sim.SetTimeStep(0.1f);

        Target tgt = new Target();
        tgt.SetTarget(desOrbit, sim);

        Upfg guidance = new Upfg();
        guidance.Setup(sim, tgt);

        Assert.Equal(4862564.5, guidance.PrevVals.rd.X);
        Assert.Equal(1835815.75, guidance.PrevVals.rd.Y);
        Assert.Equal(4181804.25, guidance.PrevVals.rd.Z);

    }

    [Fact]
    public void run_returns_expected()
    {
        float lat = 28.5f;
        string path = "/home/oli/code/csharp/upfgconsole/upfgconsole/saturnV.json";

        MissionConfig mission = Utils.ReadMission(path);
        Vehicle veh = Vehicle.FromStages(mission);
        Dictionary<string, float> desOrbit = mission.Orbit;

        double azimuth = Math.Asin(Math.Cos(Utils.DegToRad(desOrbit["inc"])) / Math.Cos(Utils.DegToRad(lat)));

        var initial = new Dictionary<string, double>
        {
            {"altitude", 45 },
            {"fpa", 50 },
            {"speed", 2400 },
            {"latitude", lat},
            {"longitude", 0 },
            {"heading", Utils.RadToDeg(azimuth) }
        };

        Simulator sim = new Simulator();
        sim.SetVesselStateFromLatLong(initial);
        sim.SetVehicle(veh);
        sim.SetTimeStep(0.1f);

        Target tgt = new Target();
        tgt.SetTarget(desOrbit, sim);

        Upfg guidance = new Upfg();
        guidance.Setup(sim, tgt);

        guidance.Run(sim, tgt, veh);

        Assert.Equal(544.66, guidance.PrevVals.tgo, 2);
        Assert.Equal(1455633.12, guidance.PrevVals.rgrav.Length(), 2);
        Assert.Equal(6450.63037, guidance.PrevVals.vgo.Length(), 2);

    }
}
