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

public class Simulator
{
    public Vehicle SimVehicle { get; set; }
    public Environment Env { get; set; }
    public Dictionary<string, object> Mission { get; private set; }
    public SimState State { get; private set; }
    public Dictionary<string, Vector3> Iteration { get; private set; }
    public float dt { get; private set; }
    public Vector3 ThrustVector { get; private set; }
    public List<SimState> History { get; set; }


    public Simulator()
    {
        SimVehicle = null;
        Env = null;
        Mission = null;
        State = new SimState();
        State.CalcMiscParams();
        State.CartToKepler();
        Iteration = new Dictionary<string, Vector3>();
        dt = 1;
        ThrustVector = new Vector3(0, 0, 0);

        History = new List<SimState>();

    }

    public void SetVehicle(Vehicle vehiclein)
    {
        SimVehicle = vehiclein;
        State.mass = (float)SimVehicle.Stages[0].MassTotal;
    }

    public SimState GetVesselState()
    {
        return State;
    }

    public void SetVesselStateFromLatLong(Dictionary<string, double> initial)
    {
        double altitude = initial["altitude"];
        double fpa = initial["fpa"];
        double latitude = initial["latitude"];
        double longitude = initial["longitude"];
        double heading = initial["heading"];
        double speed = initial["speed"];

        fpa = fpa * Math.PI / 180.0;
        heading = Utils.DegToRad(heading);

        double r = Constants.Re + altitude * 1000;

        State.r = Utils.SphericalToCartesian(latitude, longitude, r);
        State.v = Utils.ComputeVelocity(State.r, speed, fpa, heading);
        State.t = 0;

        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void SetTimeStep(float timestep)
    {
        dt = timestep;
    }

    public void SetGuidance(Vector3 thrustvector, Stage stage)
    {
        ThrustVector = thrustvector * (float)stage.Thrust;
    }

    private void CalcAccel()
    {
        Vector3 r21 = State.r;
        Iteration["a"] = Utils.CalcGravVector(Constants.Mu, r21)
        + ThrustVector / State.mass;

    }

    private void CalcVel()
    {
        Iteration["v"] = State.v + (Vector3)Iteration["a"] * dt;
    }

    private void CalcPos()
    {
        Iteration["r"] = State.r + (Vector3)Iteration["v"] * dt;
    }

    private void UpdateStateHistory()
    {

        History.Add(State);


        State.r = Iteration["r"];
        State.v = Iteration["v"];
        State.t = State.t + dt;
        State.mass = State.mass - (float)(SimVehicle.Stages[0].Thrust / (Constants.g0 * SimVehicle.Stages[0].Isp));
        State.CartToKepler();
        State.CalcMiscParams();

    }

    public void StepForward()
    {
        CalcAccel();
        CalcVel();
        CalcPos();
        UpdateStateHistory();

    }

}

public class Target
{
    public float radius { get; private set; } = 0;
    public float velocity { get; private set; } = 0;
    public float fpa { get; private set; } = 0;
    public Vector3 normal { get; private set; } = new Vector3(0, 0, 0);
    public float inc { get; private set; } = 0;
    public float LAN { get; private set; } = 0;

    public void SetTarget(Dictionary<string, float> targetParams, Simulator sim)
    {
        SimState state = sim.State;

        float pe = targetParams["pe"] * 1000 + Constants.Re;
        float ap = targetParams["ap"] * 1000 + Constants.Re;

        radius = pe;
        float sma = (pe + ap) / 2;
        float vpe = (float)Math.Pow(Constants.Mu * (2 / pe - 1 / sma), 0.5);
        velocity = vpe;
        float srm = pe * vpe;
        fpa = (float)Math.Acos(srm / srm);

        inc = targetParams["inc"];

        if (targetParams.ContainsKey("LAN"))
        {
            LAN = targetParams["LAN"];
        }
        else
        {
            LAN = (float)(state.Misc["longitude"] - Math.Asin(Math.Tan(Math.PI/2 - Utils.DegToRad(inc)) * Math.Tan(state.Misc["latitude"])));
        }

        normal = Utils.CalcOrbitNormal(inc, LAN);

    }

}

public class SimState
{
    public Vector3 r { get; set; } = new Vector3(0, 0, 0);
    public Vector3 v { get; set; } = new Vector3(0, 0, 0);
    public float t { get; set; } = 0;
    public float mass { get; set; } = 1e5f;
    public Dictionary<string, double> Kepler { get; set; } = new Dictionary<string, double> { };
    public Dictionary<string, double> Misc { get; set; } = new Dictionary<string, double> { };

    public void CalcMiscParams()
    {
        Misc = new Dictionary<string, double>
        {
            {"longitude", Math.Atan2(r.Y, r.X)},
            {"latitude", Math.Atan2(r.Z, Math.Pow(r.X * r.X + r.Y * r.Y, 0.5))},
            {"altitude", Math.Pow(r.X * r.X + r.Y * r.Y + r.Z * r.Z, 0.5) - Constants.Re}
        };
    }

    public void CartToKepler()
    {
        
        Vector3 rdot = v;

        double mu = Constants.Mu;

        Vector3 h = Vector3.Cross(r, rdot);
        double hMag = h.Length();

        Vector3 eVec = Vector3.Cross(rdot, h) / Constants.Mu - r / r.Length();
        double eMag = eVec.Length();

        Vector3 n = Vector3.Cross(new Vector3(0, 0, 1), h);
        double nMag = n.Length();

        double i = Math.Acos(h.Z / hMag);

        // True anomaly
        double v_ = Math.Acos(Vector3.Dot(eVec, r) / (eMag * r.Length()));
        if (Vector3.Dot(r, rdot) < 0)
        {
            v_ = 2 * Math.PI - v_;
        }

        // Eccentric anomaly
        double tan_v2 = Math.Tan(v_ / 2);
        double E = 2 * Math.Atan2(tan_v2 * Math.Sqrt(1 - eMag), Math.Sqrt(1 + eMag));

        // RAAN (Longitude of Ascending Node)
        double LAN = Math.Acos(n.X / nMag);
        if (n.Y < 0)
        {
            LAN = 2 * Math.PI - LAN;
        }
        if (nMag == 0)
        {
            LAN = 0;
        }

        // Argument of Periapsis
        double omega = Math.Acos(Vector3.Dot(n, eVec) / (nMag * eMag));
        if (eVec.Z < 0)
        {
            omega = 2 * Math.PI - omega;
        }
        if (nMag == 0)
        {
            omega = 0;
        }

        // Mean anomaly
        double M = E - eMag * Math.Sin(E);

        // Semi-major axis
        double a = 1 / ((2 / r.Length()) - (Vector3.Dot(rdot, rdot) / mu));

        Kepler = new Dictionary<string, double>
        {
            { "a", a },
            { "e", eMag },
            { "omega", Utils.RadToDeg(omega) },
            { "LAN", Utils.RadToDeg(LAN) },
            { "i", Utils.RadToDeg(i) },
            { "M", Utils.RadToDeg(M) }

        };
    }

}

// public class SimHistory
// {
//     public List<SimState> r { get; set; } = new List<SimState>();
// }

public static class Constants
{
    public static float Re { get; private set; } = 6371000;  // Example Earth radius in meters, replace with actual value
    public static float Mu { get; private set; } = 3.986e14f;
    public static float g0 { get; private set; } = 9.80665f;
}

public static class Utils
{
    public static Vector3 SphericalToCartesian(double latitude, double longitude, double r)
    {
        double rX = r * Math.Cos(latitude) * Math.Cos(longitude);
        double rY = r * Math.Cos(latitude) * Math.Sin(longitude);
        double rZ = r * Math.Sin(latitude);

        return new Vector3((float)rX, (float)rY, (float)rZ);

    }

    // Get the East unit vector from position in ECI
    public static Vector3 GetEastUnit(Vector3 position)
    {
        Vector3 east = new Vector3(-position.Y, position.X, 0);
        return Vector3.Normalize(east);
    }

    // Get the North unit vector from position in ECI
    public static Vector3 GetNorthUnit(Vector3 position)
    {
        Vector3 rHat = Vector3.Normalize(position);
        Vector3 eastHat = GetEastUnit(position);
        Vector3 hHat = Vector3.Normalize(Vector3.Cross(rHat, eastHat)); // Up direction
        Vector3 north = Vector3.Cross(hHat, eastHat);
        return Vector3.Normalize(north);
    }

    // Compute velocity vector from position, speed, flight path angle, and heading
    public static Vector3 ComputeVelocity(Vector3 position, double speed, double gamma, double heading)
    {
        Vector3 rHat = Vector3.Normalize(position);
        Vector3 eastHat = GetEastUnit(position);
        Vector3 northHat = GetNorthUnit(position);

        // Local horizontal direction
        Vector3 horizontalDirection = (float)(Math.Cos(heading)) * northHat + (float)(Math.Sin(heading)) * eastHat;

        // Final velocity vector
        Vector3 velocityDirection = (float)(Math.Cos(gamma)) * horizontalDirection + (float)(Math.Sin(gamma)) * rHat;

        return (float)speed * velocityDirection;
    }



    public static Vector3 CalcOrbitNormal(float inc, float omega)
    {
        inc = (float)DegToRad(inc); omega = (float)DegToRad(omega);

        Vector3 vout = new Vector3(0, 0, 1);
        vout.X = (float)(Math.Sin(inc) * Math.Sin(omega));
        vout.Y = (float)(-Math.Sin(inc) * Math.Cos(omega));
        vout.Z = (float)Math.Cos(inc);

        return vout;

    }
    public static Vector3 CalcGravVector(float mu, Vector3 r21)
    {
        Vector3 a = Constants.Mu * -r21 / (float)Math.Pow(r21.Length(), 3);

        return a;
    }
    public static Vector3 RodriguesRotation(Vector3 vin, Vector3 axis, double angle)
    {
        Vector3 vout;

        double angleRad = DegToRad(angle);
        double cosAngle = Math.Cos(angleRad);
        double sinAngle = Math.Sin(angleRad);

        vout = vin * (float)cosAngle
            + Vector3.Cross(axis, vin) * (float)sinAngle
            + axis * Vector3.Dot(axis, vin) * (float)(1 - cosAngle);

        return vout;
    }

    

    public static double DegToRad(double degrees)
    {
        double rad = degrees * Math.PI / 180.0;

        return rad;
    }
    
    public static double RadToDeg(double rad)
    {
        double degrees = rad /( Math.PI / 180.0);

        return degrees;
    }

    public static void PlotOrbit(Dictionary<string, double> elements)
    {
        // double DegToRad(double degrees) => degrees * Math.PI / 180.0;

        // Convert degrees to radians
        double omega = DegToRad(elements["omega"]);
        double LAN = DegToRad(elements["LAN"]);
        double i = DegToRad(elements["i"]);
        double M0 = DegToRad(elements["M"]);
        double a = elements["a"];
        double e = elements["e"];

        // Solve Kepler's equation
        double SolveKepler(double M, double ecc, double tol = 1e-8)
        {
            double E = ecc < 0.8 ? M : Math.PI;
            for (int iter = 0; iter < 100; iter++)
            {
                double delta = (E - ecc * Math.Sin(E) - M) / (1 - ecc * Math.Cos(E));
                E -= delta;
                if (Math.Abs(delta) < tol) break;
            }
            return E;
        }

        // Generate orbit points
        int numPoints = 500;
        double[] Mvals = Linspace(0, 2 * Math.PI, numPoints);
        double[] Evals = Mvals.Select(M => SolveKepler(M, e)).ToArray();

        double[] nuVals = Evals.Select(E =>
            2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2))
        ).ToArray();

        double[] radii = Evals.Select(E => a * (1 - e * Math.Cos(E))).ToArray();

        // Position in perifocal frame
        double[] x_p = radii.Zip(nuVals, (r, nu) => r * Math.Cos(nu)).ToArray();
        double[] y_p = radii.Zip(nuVals, (r, nu) => r * Math.Sin(nu)).ToArray();
        double[] z_p = new double[numPoints]; // all zeros

        // Rotation matrix
        double cosO = Math.Cos(LAN);
        double sinO = Math.Sin(LAN);
        double cosw = Math.Cos(omega);
        double sinw = Math.Sin(omega);
        double cosi = Math.Cos(i);
        double sini = Math.Sin(i);

        double[,] R = new double[3, 3]
        {
            { cosO * cosw - sinO * sinw * cosi, -cosO * sinw - sinO * cosw * cosi, sinO * sini },
            { sinO * cosw + cosO * sinw * cosi, -sinO * sinw + cosO * cosw * cosi, -cosO * sini },
            { sinw * sini, cosw * sini, cosi }
        };

        // Rotate to inertial frame
        double[][] orbit = new double[3][] { new double[numPoints], new double[numPoints], new double[numPoints] };

        double[] xp = new double[numPoints];
        double[] yp = new double[numPoints];
        double[] zp = new double[numPoints];

        for (int idx = 0; idx < numPoints; idx++)
        {
            double x = x_p[idx];
            double y = y_p[idx];
            double z = z_p[idx];

            xp[idx] = orbit[0][idx] = R[0, 0] * x + R[0, 1] * y + R[0, 2] * z;
            yp[idx] = R[1, 0] * x + R[1, 1] * y + R[1, 2] * z;
            zp[idx] = R[2, 0] * x + R[2, 1] * y + R[2, 2] * z;
        }

        // Visualization: Placeholder - let me know if you want this as a WPF 3D or Unity project!
        Console.WriteLine("Orbit points computed. Add your plotting logic here.");
        ScottPlot.Plot XYplot = new();
        XYplot.Add.Scatter(xp, yp);
        XYplot.Add.Scatter(xp, zp);
        XYplot.SavePng("XY.png", 400, 300);

    }

    private static double[] Linspace(double start, double end, int num)
    {
        double[] result = new double[num];
        double step = (end - start) / (num - 1);
        for (int i = 0; i < num; i++)
        {
            result[i] = start + step * i;
        }
        return result;
    }
}

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
    public List<Stage> Stages { get; set; }
    
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




public class Environment
{
    // Environment properties here
}