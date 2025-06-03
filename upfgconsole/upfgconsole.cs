// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security;
using ScottPlot.LayoutEngines;
using lib;
using System.Reflection;

class Handler
{
    static void Main()
    {
        Console.WriteLine("Hello, World!");

        


        float inc = 51.6f;
        float lat = 28.5f;

        Dictionary<string, float> desOrbit = new Dictionary<string, float>{
            {"pe", 300 },
            {"ap", 300 },
            {"inc", inc }
        };

        double azimuth = Math.Asin(Math.Cos(Utils.DegToRad(inc)) / Math.Cos(Utils.DegToRad(lat)));

        Dictionary<string, double> initial = new Dictionary<string, double>
        {
            {"altitude", 20 },
            {"fpa", 45 },
            {"speed", 2000 },
            {"latitude", lat},
            {"longitude", 0 },
            {"heading", Utils.RadToDeg(azimuth) }

        };


        Vehicle veh = Vehicle.FromJson("/home/oli/code/csharp/upfgconsole/upfgconsole/test_veh.json");

        Simulator sim = new Simulator();
        sim.SetVesselStateFromLatLong(initial);
        sim.SetVehicle(veh);

        sim.State.mass = (float)veh.Stages[0].MassTotal;

        Target tgt = new Target();
        tgt.SetTarget(desOrbit, sim);
        
        Upfg guidance = new Upfg();
        guidance.Setup(sim, tgt);

        sim.StepForward();

        List<float> simX = new List<float>();
        List<float> simY = new List<float>();
        List<float> simAlt = new List<float>();

        double trem = 13;

        Console.WriteLine(
            "{0,8} {1,8} {2,8} {3,8}  {4,8} {5,8} {6,8}  {7,8} {8,8} {9,8}  {10,8} {11,8} {12,8}",
            "Tgo",
            "VgoX", "VgoY", "VgoZ",
            "RdX", "RdY", "RdZ",
            "GravX", "GravY", "GravZ",
            "BiasX", "BiasY", "BiasZ"
        );

        int iter = 0;

        while (guidance.PrevVals.tgo > trem)
        {
            while (!guidance.ConvergenceFlag)
            {
                guidance.Run(sim, tgt, veh);
                Console.WriteLine(
                "{0,8:F3} {1,8:F3} {2,8:F3} {3,8:F3}  {4,8:F3} {5,8:F3} {6,8:F3}  {7,8:F3} {8,8:F3} {9,8:F3}  {10,8:F3} {11,8:F3} {12,8:F3}",
                guidance.PrevVals.tgo,
                guidance.PrevVals.vgo.X, guidance.PrevVals.vgo.Y, guidance.PrevVals.vgo.Z,
                guidance.PrevVals.rd.X, guidance.PrevVals.rd.Y, guidance.PrevVals.rd.Z,
                guidance.PrevVals.rgrav.X, guidance.PrevVals.rgrav.Y, guidance.PrevVals.rgrav.Z,
                guidance.PrevVals.rbias.X, guidance.PrevVals.rbias.Y, guidance.PrevVals.rbias.Z
            );
            if (iter > 1000)
            {
                    return; //throw new Exception("Exceeded max iterations");
            }
            iter++;
            }

            guidance.Run(sim, tgt, veh);

            if (iter > 1000)
            {
                return;  //throw new Exception("Exceeded max iterations");
            }

            sim.SetGuidance(guidance.Steering, veh.Stages[0]);
            sim.StepForward();

            Console.WriteLine(
                "{0,8:F3} {1,8:F3} {2,8:F3} {3,8:F3}  {4,8:F3} {5,8:F3} {6,8:F3}  {7,8:F3} {8,8:F3} {9,8:F3}  {10,8:F3} {11,8:F3} {12,8:F3}",
                guidance.PrevVals.tgo,
                guidance.PrevVals.vgo.X, guidance.PrevVals.vgo.Y, guidance.PrevVals.vgo.Z,
                guidance.PrevVals.rd.X, guidance.PrevVals.rd.Y, guidance.PrevVals.rd.Z,
                guidance.PrevVals.rgrav.X, guidance.PrevVals.rgrav.Y, guidance.PrevVals.rgrav.Z,
                guidance.PrevVals.rbias.X, guidance.PrevVals.rbias.Y, guidance.PrevVals.rbias.Z
            );



            Vector3 r = sim.State.r;

            simX.Add(sim.State.t);
            simY.Add((float)sim.State.Misc["altitude"]);

            iter++;

        }

        Console.WriteLine(simX);

        ScottPlot.Plot myPlot = new();
        myPlot.Add.Scatter(simX, simY);
        myPlot.SavePng("test.png", 400, 300);

        Dictionary<string, double> kepler = sim.State.Kepler;

        Utils.PlotOrbit(kepler);
    }



}

