using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using lib;

namespace lib_tests;

public class SimulatorTests
{
    [Fact]
    public void Simulator_InitializesCorrectly()
    {
        var sim = new Simulator();

        Assert.NotNull(sim.SimVehicle);
        Assert.NotNull(sim.Mission);
        Assert.NotNull(sim.State);
        Assert.NotNull(sim.Iteration);
        Assert.Equal(1, sim.dt);
        Assert.Equal(Vector3.Zero, sim.ThrustVector);
        Assert.Empty(sim.History);
    }

    [Fact]
    public void SetVehicle_UpdatesMass()
    {
        var sim = new Simulator();
        var stage = new Stage { MassTotal = 1234.5, Thrust = 500, Isp = 300 };
        sim.SetVehicle(new Vehicle { Stages = new List<Stage> { stage } });

        Assert.Equal(1234.5f, sim.GetVesselState().mass, 1);
    }

    [Fact]
    public void SetVesselStateFromLatLong_InitializesPositionAndVelocity()
    {
        var sim = new Simulator();
        var initial = new Dictionary<string, double>
        {
            { "altitude", 400 }, // km
            { "fpa", 0 },
            { "latitude", 0 },
            { "longitude", 0 },
            { "heading", 90 },
            { "speed", 7800 } // m/s
        };

        sim.SetVesselStateFromLatLongAir(initial);

        Assert.NotEqual(Vector3.Zero, sim.GetVesselState().r);
        Assert.NotEqual(Vector3.Zero, sim.GetVesselState().v);
        Assert.Equal(Constants.Re + 400 * 1e3f, sim.State.r.X);
        Assert.Equal(0, sim.State.r.Y);
        Assert.Equal(0, sim.State.r.Z);
        Assert.Equal(0, sim.GetVesselState().t);
    }

    [Fact]
    public void StepForward_UpdatesStateAndHistory()
    {
        var sim = new Simulator();
        var stage = new Stage { MassTotal = 50000, Thrust = 1e6, Isp = 300 };
        sim.SetVehicle(new Vehicle { Stages = new List<Stage> { stage } });
        sim.SetTimeStep(1);
        sim.SetGuidance(Vector3.UnitX, stage);

        var initialPosition = sim.GetVesselState().r;
        sim.StepForward();

        Assert.NotEqual(initialPosition, sim.GetVesselState().r);
        Assert.Single(sim.History);
    }
}

public class SimStateTests
{
    [Fact]
    public void CalcMiscParams_ComputesCorrectValues()
    {
        var state = new SimState
        {
            r = new Vector3(Constants.Re + 1000, 0, 0) // 1 km altitude at equator
        };

        state.CalcMiscParams();

        Assert.True(state.Misc.ContainsKey("longitude"));
        Assert.True(state.Misc.ContainsKey("latitude"));
        Assert.True(state.Misc.ContainsKey("altitude"));
        Assert.Equal(1000, state.Misc["altitude"], 0); // Altitude in meters
    }

    [Fact]
    public void CartToKepler_ComputesKeplerianElements()
    {
        var state = new SimState
        {
            r = new Vector3((float)(Constants.Re + 400000), 0, 0),
            v = new Vector3(0, 7670, 0)
        };

        state.CartToKepler();

        Assert.True(state.Kepler.ContainsKey("a"));
        Assert.True(state.Kepler.ContainsKey("e"));
        Assert.True(state.Kepler.ContainsKey("omega"));
        Assert.True(state.Kepler.ContainsKey("LAN"));
        Assert.True(state.Kepler.ContainsKey("i"));
        Assert.True(state.Kepler.ContainsKey("M"));
    }
}
