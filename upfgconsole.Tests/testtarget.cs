using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using lib;
namespace lib_tests;

public class TargetTests
{
    [Fact]
    public void SetTarget_AssignsExpectedOrbitalParameters()
    {
        // Arrange
        var sim = new Simulator();
        sim.SetVesselStateFromLatLongAir(new Dictionary<string, double>
        {
            {"altitude", 0},
            {"fpa", 0},
            {"latitude", 0},
            {"longitude", 0},
            {"heading", 90},
            {"speed", 7800}
        });

        var target = new UPFGTarget(); // Assume this is your class

        var targetParams = new Dictionary<string, float>
        {
            {"pe", 200},   // km
            {"ap", 200},   // km
            {"inc", 28.5f},
            {"LAN", 45f}
        };

        // Act
        target.Set(targetParams, sim);

        // Assert
        float expectedRadius = 200_000f + Constants.Re;
        Assert.Equal(expectedRadius, target.radius, 1); // allow slight float error
        Assert.True(target.velocity > 7000 && target.velocity < 8000); // rough check
        Assert.Equal(0f, target.fpa, 5); // should be horizontal
        Assert.Equal(45f, target.LAN, 5); // set LAN
        Assert.True(Math.Abs(target.normal.Length() - 1) < 1e-5); // normal should be unit vector
    }

    [Fact]
    public void SetTarget_UsesLongitudeWhenLANMissing()
    {
        // Arrange
        var sim = new Simulator();
        sim.SetVesselStateFromLatLongAir(new Dictionary<string, double>
        {
            {"altitude", 0},
            {"fpa", 0},
            {"latitude", 45},
            {"longitude", 0}, // radian
            {"heading", 90},
            {"speed", 7800}
        });

        var target = new UPFGTarget();

        var targetParams = new Dictionary<string, float>
        {
            {"pe", 200},
            {"ap", 200},
            {"inc", 45f} // equatorial
            // LAN not included
        };

        // Act
        target.Set(targetParams, sim);

        // Assert
        Assert.Equal(-Math.PI/2, target.LAN, 5);
    }
}
