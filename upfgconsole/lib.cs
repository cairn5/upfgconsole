namespace lib;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using ScottPlot.LayoutEngines;

public class Simulator
{
    public Vehicle Veh { get; set; }
    public Environment Env { get; set; }
    public Dictionary<string, object> Mission { get; private set; }
    public SimState State { get; private set; }
    public Dictionary<string, Vector3> Iteration { get; private set; }
    public float dt { get; private set; }
    public Vector3 ThrustVector { get; private set; }
    public List<SimState> History { get; set; }


    public Simulator()
    {
        Veh = null;
        Env = null;
        Mission = null;
        State = new SimState();
        Iteration = new Dictionary<string, Vector3>();
        dt = 1;
        ThrustVector = new Vector3(0, 0, 0);

        History = new List<SimState>();

    }



    public SimState GetVesselState()
    {
        return State;
    }

    public void SetVesselStateFromMidair(double altitude, double velocity, double fpa)
    {

        double radFpa = fpa * Math.PI / 180.0;

        State.r = new Vector3(0, (float)(Constants.Re + altitude * 1000.0), 0);
        State.v = new Vector3((float)(velocity * Math.Cos(radFpa)), (float)(velocity * Math.Sin(radFpa)), 0);
        State.t = 0;

        // State = new Dictionary<string, object>
        // {
        //     { "r", new Vector3(0, (float)(Constants.Re + altitude * 1000.0), 0) },
        //     { "v", new Vector3((float)(velocity * Math.Cos(radFpa)), (float)(velocity * Math.Sin(radFpa)), 0) },
        //     { "t", (float)0.0 }
        // };

    }

    public void SetTimeStep(float timestep)
    {
        dt = timestep;
    }

    public void SetGuidance(Vector3 thrustvector)
    {
        ThrustVector = thrustvector;
    }

    private void CalcAccel(float mass)
    {
        Vector3 r21 = State.r;
        Iteration["a"] = Constants.Mu * -r21 / (float)Math.Pow(r21.Length(), 3);
        //+ ThrustVector / mass;

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

    }

    public void StepForward(float mass)
    {
        CalcAccel(mass);
        CalcVel();
        CalcPos();
        UpdateStateHistory();

    }

}

public class SimState
{
    public Vector3 r{ get; set; }
    public Vector3 v{ get; set; }
    public float t{ get; set; }     
    
}

public class SimHistory
{
    public List<SimState> r { get; set; } = new List<SimState>();
}

public static class Constants
{
    public static float Re = 6371000;  // Example Earth radius in meters, replace with actual value
    public static float Mu = 3.986e14F;
}

public static class Utils
{
    public static void PlotOrbit(Dictionary<string, double> elements)
    {
        double DegToRad(double degrees) => degrees * Math.PI / 180.0;

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


// Placeholder classes for Vehicle and Environment
public class Vehicle
{
    // Vehicle properties here
}

public class Environment
{
    // Environment properties here
}