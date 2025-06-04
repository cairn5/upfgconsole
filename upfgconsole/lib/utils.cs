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

        latitude = DegToRad(latitude);
        longitude = DegToRad(longitude);

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

    public static (float b, float c) CalculateSphericalAAS(float a, float beta, float alpha)
    {
        // alpha = 90 - alpha
        double b = Math.Asin((Math.Sin(a) * Math.Sin(beta)) / Math.Sin(alpha));

        double tanaMb = Math.Tan(0.5f * (a - b));
        double sinalphaPbeta = Math.Tan(0.5f * (alpha + beta));
        double sinalphaMbeta = Math.Tan(0.5f * (alpha - beta));

        double c = 2 * Math.Atan(tanaMb * sinalphaPbeta / sinalphaMbeta);

        //alternative approach
        b = Math.Asin(Math.Tan(a) / Math.Tan(alpha));

        return ((float)b, (float)c);
    }

    public static Vector3 CalcOrbitNormal(float inc, float LAN)
    {
        //inc = (float)DegToRad(inc); LAN = (float)DegToRad(LAN);

        Vector3 vout = new Vector3(0, 0, 1);
        vout.X = (float)(Math.Sin(inc) * Math.Sin(LAN));
        vout.Y = (float)(-Math.Sin(inc) * Math.Cos(LAN));
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





public class Environment
{
    // Environment properties here
}