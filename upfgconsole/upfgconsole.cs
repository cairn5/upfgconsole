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

        float inc = 58.5f;
        float lat = 28.5f;

        Dictionary<string, float> desOrbit = new Dictionary<string, float>{
            {"pe", 300 },
            {"ap", 300 },
            {"inc", inc }
        };

        double azimuth = Math.Asin(Math.Cos(Utils.DegToRad(inc)) / Math.Cos(Utils.DegToRad(lat)));

        Dictionary<string, double> initial = new Dictionary<string, double>
        {
            {"altitude", 45 },
            {"fpa", 50 },
            {"speed", 2400 },
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

        double trem = 2;

        Utils.PrintParamHeader();


        int iter = 0;

        while (guidance.PrevVals.tgo > trem)
        {
            
            if (iter > 1000)
            {
                Console.WriteLine("UPFG FAILED");
                return;  //throw new Exception("Exceeded max iterations");
            }

            guidance.Run(sim, tgt, veh);

            // Utils.PrintParams(guidance);


            Console.WriteLine(sim.State.mass);

            if (guidance.ConvergenceFlag)
            {
                sim.SetGuidance(guidance.Steering, veh.Stages[0]);
                sim.StepForward();
            }


            iter++;

        }

        Utils.PlotTrajectory(sim);

        Dictionary<string, double> kepler = sim.State.Kepler;

        Console.WriteLine(kepler["e"]);

        Utils.PlotOrbit(kepler);
    }



}

