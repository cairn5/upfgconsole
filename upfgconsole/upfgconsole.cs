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

        

        Dictionary<string, double> initial = new Dictionary<string, double>
        {
            {"altitude", 20 },
            {"fpa", 45 },
            {"speed", 2000 },
            {"latitude", 0 },
            {"longitude", 0 },
            {"heading", 90 }

        };

        Dictionary<string, float> desOrbit = new Dictionary<string, float>{
            {"pe", 200 },
            {"ap", 200 },
            {"inc", 0 }
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

        
        List<float> simX = new List<float>();
        List<float> simY = new List<float>();

        double trem = 13;

        while (guidance.PrevVals.tgo > trem)
        {
            while (!guidance.ConvergenceFlag)
            {
                guidance.Run(sim, tgt, veh);
            }

            guidance.Run(sim, tgt, veh);
            sim.SetGuidance(guidance.Steering);
            

            Vector3 r = sim.State.r;

            simX.Add(r.X);
            simY.Add(r.Y);
        }

        Console.WriteLine(simX);

        ScottPlot.Plot myPlot = new();
        myPlot.Add.Scatter(simX, simY);
        myPlot.SavePng("test.png", 400, 300);

        Dictionary<string, double> kepler = sim.State.Kepler;

        Utils.PlotOrbit(kepler);
    }



}

