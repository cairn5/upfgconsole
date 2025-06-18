using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using lib;

namespace lib_tests;

public class UtilsTests
{
    [Fact]
    public void CalculateSphericalAAS_Returns_Expected_90deg()
    {
        float a = (float)Utils.DegToRad(10); //lat
        float beta = (float)Utils.DegToRad(90);
        float alpha = (float)Utils.DegToRad(10); //inclination
        (float b, float c) = Utils.CalculateSphericalAAS(a, beta, alpha);


        //Assert.Equal(-Math.PI / 2, c);
        Assert.Equal(Math.PI / 2, b, 2);

    }

    [Fact]
    public void CalculateSphericalAAS_Returns_Expected_0deg()
    {
        float a = (float)Utils.DegToRad(0); //lat
        float beta = (float)Utils.DegToRad(90);
        float alpha = (float)Utils.DegToRad(10); //inclination
        (float b, float c) = Utils.CalculateSphericalAAS(a, beta, alpha);


        //Assert.Equal(-Math.PI / 2, c);
        Assert.Equal(0, b, 2);

    }

    [Fact]
    public void GetEastUnit_Returns_Expected()
    {
        Vector3 pos = new(1, 0, 0);
        Vector3 exp = new(0, 1, 0);

        Vector3 east = Utils.GetEastUnit(pos);

        Assert.Equal(exp, east);

    }
    
    [Fact]
    public void GetNorthUnit_Returns_Expected()
    {
        Vector3 pos = new(1, 0, 0);
        Vector3 exp = new(0, 0, 1);

        Vector3 north = Utils.GetNorthUnit(pos);

        Assert.Equal(exp, north);

    }

    [Fact]
    public void SurfaceRestECIVelocity_Equator_MatchesEarthRotation()
    {
        // Arrange: point at equator (x=Re, y=0, z=0), t=0
        float Re = Constants.Re;
        double omega = Constants.We;
        float t = 0f;
        var state = new SimState { r = new Vector3(Re, 0, 0), v = Vector3.Zero, t = t, mass = 1000 };

        // Act
        SimState eciState = Utils.SurfaceRestECIVelocity(state);
        Vector3 v_eci = eciState.v;

        // Expected: velocity is perpendicular to radius, magnitude = omega*Re, direction +Y
        float expectedVy = (float)(omega * Re);
        Assert.InRange(v_eci.X, -1, 1); // Should be near zero
        Assert.InRange(v_eci.Y, expectedVy - 1, expectedVy + 1);
        Assert.InRange(v_eci.Z, -1, 1);
    }

    [Fact]
    public void ECEFtoECI_RoundTrip_ECItoECEF()
    {
        // Arrange: arbitrary ECI state
        float Re = Constants.Re;
        double omega = Constants.We;
        float t = 1234.5f;
        var state = new SimState { r = new Vector3(Re, 0, 0), v = new Vector3(0, 1000, 0), t = t, mass = 1000 };
        // Convert to ECEF
        var ecefState = Utils.ECItoECEF(state);
        // Convert back
        var eciState2 = Utils.ECEFtoECI(ecefState);
        // Print r_ecef for debugging
        Console.WriteLine($"r_ecef: {ecefState.r}");
        // Assert round-trip accuracy (allow 1 meter tolerance)
        Assert.InRange((eciState2.r - state.r).Length(), 0, 1f);
        Assert.InRange((eciState2.v - state.v).Length(), 0, 1f);
        Assert.Equal(state.mass, eciState2.mass);
        Assert.Equal(state.t, eciState2.t);
    }

    [Fact]
    public void ECItoECEF_Equator_Eastward_90degRotation()
    {
        // Arrange: vessel at equator (x=Re, y=0, z=0), eastward velocity (v_y > 0), t such that theta=90deg
        float Re = Constants.Re;
        double omega = Constants.We;
        float t = (float)(Math.PI / 2 / omega); // theta = omega * t = 90 deg
        var state = new SimState
        {
            r = new Vector3(Re, 0, 0),
            v = new Vector3(0, 1000, 0), // 1 km/s eastward
            t = t,
            mass = 1000
        };

        // Act
        SimState ecefState = Utils.ECItoECEF(state);
        Vector3 r_ecef = ecefState.r;
        Vector3 v_ecef = ecefState.v;

        // After 90 deg rotation, position should be (0, -Re, 0)
        Assert.InRange(r_ecef.X, -1, 1); // allow 1 meter tolerance
        Assert.InRange(r_ecef.Y, -Re - 1, -Re + 1);
        Assert.InRange(r_ecef.Z, -1, 1);

        // Velocity: should be (+1000, 0, 0) in ECEF before omega correction
        // omega x r_ecef = (0,0,omega) x (0,-Re,0) = (-omega*Re, 0, 0)
        float expectedVx = 1000 - (float)(omega * Re);
        Assert.InRange(v_ecef.X, expectedVx - 1, expectedVx + 1);
        Assert.InRange(v_ecef.Y, -1, 1);
        Assert.InRange(v_ecef.Z, -1, 1);
        Assert.Equal(state.mass, ecefState.mass);
        Assert.Equal(state.t, ecefState.t);
    }

    [Fact]
    public void CalcLaunchAzimuthRotating_KSC_Inc28p5_Is90Deg()
    {
        // Arrange: KSC latitude 28.5 deg (in radians), inclination 28.5 deg (in radians)
        double latRad = Utils.DegToRad(28.5);
        double incRad = Utils.DegToRad(28.5);
        double vorbit = 7800; // typical LEO orbital velocity in m/s

        var sim = new Simulator();
        sim.State.Misc["latitude"] = latRad;
        var tgt = Activator.CreateInstance(typeof(Target), true) as Target;
        Assert.NotNull(tgt);
        var incProp = typeof(Target).GetProperty("inc");
        var velProp = typeof(Target).GetProperty("velocity");
        Assert.NotNull(incProp);
        Assert.NotNull(velProp);
        incProp.SetValue(tgt, (float)incRad);
        velProp.SetValue(tgt, (float)vorbit);

        // Act
        double az = Utils.CalcLaunchAzimuthRotating(sim, tgt);
        double azDeg = Utils.RadToDeg(az);

        // Assert: Should be 90.0 deg within 0.1 deg
        Assert.InRange(azDeg, 89.9, 90.1);
    }

    [Fact]
    public void CalcLaunchAzimuthRotating_KSC_Inc45_SensibleAzimuth()
    {
        // Arrange: KSC latitude 28.5 deg (in radians), inclination 45 deg (in radians)
        double latRad = Utils.DegToRad(28.5);
        double incRad = Utils.DegToRad(45.0);
        double vorbit = 7800; // typical LEO orbital velocity in m/s

        var sim = new Simulator();
        sim.State.Misc["latitude"] = latRad;
        var tgt = Activator.CreateInstance(typeof(Target), true) as Target;
        Assert.NotNull(tgt);
        var incProp = typeof(Target).GetProperty("inc");
        var velProp = typeof(Target).GetProperty("velocity");
        Assert.NotNull(incProp);
        Assert.NotNull(velProp);
        incProp.SetValue(tgt, (float)incRad);
        velProp.SetValue(tgt, (float)vorbit);

        // Act
        double az = Utils.CalcLaunchAzimuthRotating(sim, tgt);
        double azDeg = Utils.RadToDeg(az);

        // Assert: Should be less than 90 deg, but greater than 0 (eastward, but not due east)
        Assert.InRange(azDeg, 51.70, 51.72);
    }

}
